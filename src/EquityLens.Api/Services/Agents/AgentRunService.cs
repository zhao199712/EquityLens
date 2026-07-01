using System.Diagnostics;
using System.Text.Json;
using EquityLens.Api.Contracts.Agents;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.Agents;

public sealed class AgentRunService : IAgentRunService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly EquityLensDbContext _dbContext;
    private readonly IReadOnlyDictionary<string, IAgentWorkflowDefinitionProvider> _workflowProviders;
    private readonly IAgentWorkflowPlanner _workflowPlanner;
    private readonly IAgentRunGraphValidator _runGraphValidator;
    private readonly IReadOnlyDictionary<string, IAgentNodeHandler> _nodeHandlers;
    private readonly ILogger<AgentRunService> _logger;

    public AgentRunService(
        EquityLensDbContext dbContext,
        IEnumerable<IAgentWorkflowDefinitionProvider> workflowProviders,
        IAgentWorkflowPlanner workflowPlanner,
        IAgentRunGraphValidator runGraphValidator,
        IEnumerable<IAgentNodeHandler> nodeHandlers,
        ILogger<AgentRunService> logger)
    {
        _dbContext = dbContext;
        _workflowProviders = CreateWorkflowProviderRegistry(workflowProviders);
        _workflowPlanner = workflowPlanner;
        _runGraphValidator = runGraphValidator;
        _nodeHandlers = nodeHandlers.ToDictionary(x => x.NodeType, StringComparer.Ordinal);
        _logger = logger;
    }

    public async Task<AgentRunSummaryResponse> CreateCriticReviewAsync(
        Guid userId,
        Guid researchRunId,
        CancellationToken cancellationToken = default)
    {
        var provider = GetWorkflowProvider(AgentWorkflowTypes.CriticReview);
        var run = provider.CreateRun(userId, researchRunId);
        _dbContext.AgentRuns.Add(run);
        AddEvent(run, null, AgentEventTypes.RunCreated, "CriticReview run created.", new { researchRunId });
        await _dbContext.SaveChangesAsync(cancellationToken);

        await ExecuteAgentRunAsync(run.Id, userId, cancellationToken);
        var persisted = await _dbContext.AgentRuns.AsNoTracking().FirstAsync(x => x.Id == run.Id, cancellationToken);
        return MapSummary(persisted);
    }

    public async Task<AgentRunSummaryResponse> CreateDraftRevisionAsync(
        Guid userId,
        Guid criticReviewRunId,
        CancellationToken cancellationToken = default)
    {
        var provider = GetWorkflowProvider(AgentWorkflowTypes.DraftRevision);
        var run = provider.CreateRun(userId, criticReviewRunId);
        _dbContext.AgentRuns.Add(run);
        AddEvent(run, null, AgentEventTypes.RunCreated, "DraftRevision run created.", new { criticReviewRunId });
        await _dbContext.SaveChangesAsync(cancellationToken);

        await ExecuteAgentRunAsync(run.Id, userId, cancellationToken);
        var persisted = await _dbContext.AgentRuns.AsNoTracking().FirstAsync(x => x.Id == run.Id, cancellationToken);
        return MapSummary(persisted);
    }

    public async Task<IReadOnlyList<AgentRunSummaryResponse>> ListAsync(
        Guid? userId,
        int limit = 50,
        string? workflowType = null,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.AgentRuns.AsNoTracking();
        if (userId.HasValue)
            query = query.Where(x => x.UserId == userId.Value);
        if (!string.IsNullOrWhiteSpace(workflowType))
            query = query.Where(x => x.WorkflowType == workflowType.Trim());
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status.Trim());

        return await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(Math.Clamp(limit, 1, 100))
            .Select(x => MapSummary(x))
            .ToListAsync(cancellationToken);
    }

    public async Task<AgentRunDetailResponse?> GetByIdAsync(
        Guid id,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        var runQuery = _dbContext.AgentRuns.AsNoTracking();
        if (userId.HasValue)
            runQuery = runQuery.Where(x => x.UserId == userId.Value);

        var run = await runQuery.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (run is null) return null;

        var nodes = await _dbContext.AgentRunNodes.AsNoTracking()
            .Where(x => x.AgentRunId == id)
            .OrderBy(x => x.StartedAtUtc ?? DateTime.MaxValue)
            .ThenBy(x => x.NodeKey)
            .Select(x => new AgentRunNodeResponse(
                x.Id, x.NodeKey, x.NodeType, x.Status, x.InputJson, x.OutputJson,
                x.ErrorMessage, x.StartedAtUtc, x.CompletedAtUtc, x.DurationMs))
            .ToListAsync(cancellationToken);

        var events = await _dbContext.AgentRunEvents.AsNoTracking()
            .Where(x => x.AgentRunId == id)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => new AgentRunEventResponse(
                x.Id, x.AgentRunNodeId, x.EventType, x.Message, x.PayloadJson, x.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        var toolCalls = await _dbContext.AgentToolCalls.AsNoTracking()
            .Where(x => x.AgentRunId == id)
            .OrderBy(x => x.StartedAtUtc)
            .Select(x => new AgentToolCallResponse(
                x.Id, x.AgentRunNodeId, x.ToolName, x.Status, x.ArgumentsJson, x.ResultPreview,
                x.ResultJson, x.ErrorMessage, x.StartedAtUtc, x.CompletedAtUtc, x.DurationMs))
            .ToListAsync(cancellationToken);

        var feedback = await _dbContext.AgentFeedback.AsNoTracking()
            .Where(x => x.AgentRunId == id)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => new AgentFeedbackResponse(
                x.Id, x.AgentRunNodeId, x.FeedbackType, x.Status, x.Prompt, x.ResponseJson,
                x.CreatedAtUtc, x.RespondedAtUtc))
            .ToListAsync(cancellationToken);

        return new AgentRunDetailResponse(
            MapSummary(run), nodes, events, toolCalls, feedback,
            run.BlackboardJson, run.OutputJson, run.WorkflowDefinitionJson);
    }

    public async Task<AgentRunSummaryResponse?> RetryAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var run = await _dbContext.AgentRuns
            .Include(x => x.Nodes)
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, cancellationToken);
        if (run is null) return null;
        if (run.Status != AgentRunStatuses.Failed) return MapSummary(run);

        run.Status = AgentRunStatuses.Pending;
        run.ErrorMessage = null;
        run.OutputJson = null;
        var provider = GetWorkflowProvider(run.WorkflowType);
        run.BlackboardJson = provider.CreateInitialBlackboardJson(GetSourceRunId(run.InputJson));
        run.StartedAtUtc = null;
        run.CompletedAtUtc = null;
        foreach (var node in run.Nodes)
        {
            node.Status = AgentNodeStatuses.Pending;
            node.InputJson = null;
            node.OutputJson = null;
            node.ErrorMessage = null;
            node.StartedAtUtc = null;
            node.CompletedAtUtc = null;
            node.DurationMs = null;
        }
        AddEvent(run, null, AgentEventTypes.SupervisorDecision, "Retry requested; run reset to Pending.", new { retry = true });
        await _dbContext.SaveChangesAsync(cancellationToken);

        await ExecuteAgentRunAsync(run.Id, userId, cancellationToken);
        var persisted = await _dbContext.AgentRuns.AsNoTracking().FirstAsync(x => x.Id == id, cancellationToken);
        return MapSummary(persisted);
    }

    public async Task<AgentRunSummaryResponse?> CancelAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var run = await _dbContext.AgentRuns.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, cancellationToken);
        if (run is null) return null;
        if (run.Status is AgentRunStatuses.Succeeded or AgentRunStatuses.Cancelled)
            return MapSummary(run);

        run.Status = AgentRunStatuses.Cancelled;
        run.CompletedAtUtc = DateTime.UtcNow;
        AddEvent(run, null, AgentEventTypes.RunCancelled, "Run cancelled by user.", null);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapSummary(run);
    }

    private async Task ExecuteAgentRunAsync(Guid runId, Guid userId, CancellationToken cancellationToken)
    {
        var run = await _dbContext.AgentRuns
            .Include(x => x.Nodes)
            .Include(x => x.Events)
            .Include(x => x.ToolCalls)
            .FirstAsync(x => x.Id == runId && x.UserId == userId, cancellationToken);

        try
        {
            run.Status = AgentRunStatuses.Running;
            run.StartedAtUtc ??= DateTime.UtcNow;
            AddEvent(run, null, AgentEventTypes.RunStarted, $"{run.WorkflowType} run started.", null);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var executionOrder = _workflowPlanner.GetExecutionOrder(run.WorkflowDefinitionJson);
            _runGraphValidator.Validate(run, executionOrder);
            foreach (var nodeKey in executionOrder)
            {
                await RunNodeAsync(run, nodeKey, cancellationToken);
            }

            run.Status = AgentRunStatuses.Succeeded;
            run.CompletedAtUtc = DateTime.UtcNow;
            AddEvent(run, null, AgentEventTypes.RunSucceeded, $"{run.WorkflowType} run succeeded.", null);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Agent run {AgentRunId} failed", runId);
            run.Status = AgentRunStatuses.Failed;
            run.ErrorMessage = exception.Message;
            run.CompletedAtUtc = DateTime.UtcNow;
            AddEvent(run, null, AgentEventTypes.RunFailed, $"{run.WorkflowType} run failed.", new { error = exception.Message });
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task RunNodeAsync(AgentRun run, string nodeKey, CancellationToken cancellationToken)
    {
        var node = run.Nodes.First(x => x.NodeKey == nodeKey);
        var decisionPayload = new { decision = "RunNode", nextNodeId = nodeKey, reason = "Previous dependencies are satisfied.", mode = "Deterministic" };
        AddEvent(run, node, AgentEventTypes.SupervisorDecision, $"Supervisor selected {nodeKey}.", decisionPayload);

        node.Status = AgentNodeStatuses.Ready;
        AddEvent(run, node, AgentEventTypes.NodeReady, $"Node {nodeKey} is ready.", null);
        node.Status = AgentNodeStatuses.Running;
        node.StartedAtUtc = DateTime.UtcNow;
        AddEvent(run, node, AgentEventTypes.NodeStarted, $"Node {nodeKey} started.", null);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (!_nodeHandlers.TryGetValue(node.NodeType, out var handler))
            {
                throw new InvalidOperationException($"Unsupported node type '{node.NodeType}'.");
            }

            await handler.ExecuteAsync(new AgentNodeExecutionContext(_dbContext, run, node, AddEvent), cancellationToken);

            stopwatch.Stop();
            node.Status = AgentNodeStatuses.Succeeded;
            node.CompletedAtUtc = DateTime.UtcNow;
            node.DurationMs = stopwatch.ElapsedMilliseconds;
            AddEvent(run, node, AgentEventTypes.NodeCompleted, $"Node {nodeKey} completed.", new { durationMs = node.DurationMs });
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            node.Status = AgentNodeStatuses.Failed;
            node.ErrorMessage = exception.Message;
            node.CompletedAtUtc = DateTime.UtcNow;
            node.DurationMs = stopwatch.ElapsedMilliseconds;
            AddEvent(run, node, AgentEventTypes.NodeFailed, $"Node {nodeKey} failed.", new { error = exception.Message });
            throw;
        }
    }

    private static IReadOnlyDictionary<string, IAgentWorkflowDefinitionProvider> CreateWorkflowProviderRegistry(
        IEnumerable<IAgentWorkflowDefinitionProvider> workflowProviders)
    {
        var registry = new Dictionary<string, IAgentWorkflowDefinitionProvider>(StringComparer.Ordinal);
        foreach (var provider in workflowProviders)
        {
            if (!registry.TryAdd(provider.WorkflowType, provider))
            {
                throw new InvalidOperationException($"Workflow provider '{provider.WorkflowType}' is registered more than once.");
            }
        }

        return registry;
    }

    private IAgentWorkflowDefinitionProvider GetWorkflowProvider(string workflowType) =>
        _workflowProviders.TryGetValue(workflowType, out var provider)
            ? provider
            : throw new InvalidOperationException($"Workflow provider '{workflowType}' is not registered.");

    private static Guid GetSourceRunId(string inputJson)
    {
        using var document = JsonDocument.Parse(inputJson);
        var root = document.RootElement;
        if (root.TryGetProperty("researchRunId", out var researchRunId))
        {
            return researchRunId.GetGuid();
        }
        if (root.TryGetProperty("criticReviewRunId", out var criticReviewRunId))
        {
            return criticReviewRunId.GetGuid();
        }

        throw new InvalidOperationException("Agent run input is missing source id.");
    }

    private void AddEvent(AgentRun run, AgentRunNode? node, string eventType, string? message, object? payload)
    {
        _dbContext.AgentRunEvents.Add(new AgentRunEvent
        {
            Id = Guid.NewGuid(),
            AgentRunId = run.Id,
            AgentRunNodeId = node?.Id,
            EventType = eventType,
            Message = message,
            PayloadJson = payload is null ? null : Serialize(payload),
            CreatedAtUtc = DateTime.UtcNow
        });
    }

    private static AgentRunSummaryResponse MapSummary(AgentRun run) => new(
        run.Id,
        run.WorkflowType,
        run.AgentType,
        run.Status,
        run.CreatedAtUtc,
        run.StartedAtUtc,
        run.CompletedAtUtc,
        run.ErrorMessage);

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, SerializerOptions);
}
