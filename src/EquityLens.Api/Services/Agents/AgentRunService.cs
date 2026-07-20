using System.Text.Json;
using System.Text.Json.Nodes;
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
    private readonly IAgentRunStateMachine _runStateMachine;
    private readonly IAgentNodeStateMachine _nodeStateMachine;
    private readonly IAgentRunQueue _agentRunQueue;
    private readonly IAgentWorkflowAdminService? _workflowAdminService;
    private readonly PortfolioDiagnosisWorkflowDefinitionProvider? _portfolioDiagnosisProvider;
    private readonly IAgentWorkflowCatalog _catalog;

    public AgentRunService(
        EquityLensDbContext dbContext,
        IEnumerable<IAgentWorkflowDefinitionProvider> workflowProviders,
        IAgentRunStateMachine runStateMachine,
        IAgentNodeStateMachine nodeStateMachine,
        IAgentRunQueue agentRunQueue,
        IAgentWorkflowAdminService? workflowAdminService = null,
        PortfolioDiagnosisWorkflowDefinitionProvider? portfolioDiagnosisProvider = null,
        IAgentWorkflowCatalog? catalog = null)
    {
        _dbContext = dbContext;
        _workflowProviders = CreateWorkflowProviderRegistry(workflowProviders);
        _runStateMachine = runStateMachine;
        _nodeStateMachine = nodeStateMachine;
        _agentRunQueue = agentRunQueue;
        _workflowAdminService = workflowAdminService;
        _portfolioDiagnosisProvider = portfolioDiagnosisProvider;
        _catalog = catalog ?? new AgentWorkflowCatalog();
    }

    public async Task<AgentRunSummaryResponse> CreateCriticReviewAsync(
        Guid userId,
        Guid researchRunId,
        CancellationToken cancellationToken = default)
    {
        if (_workflowAdminService is not null) await _workflowAdminService.EnsureEnabledAsync(AgentWorkflowTypes.CriticReview, cancellationToken);
        var provider = GetWorkflowProvider(AgentWorkflowTypes.CriticReview);
        var run = provider.CreateRun(userId, researchRunId);
        await SnapshotExecutionPoliciesAsync(run, cancellationToken);
        _dbContext.AgentRuns.Add(run);
        AddEvent(run, null, AgentEventTypes.RunCreated, "CriticReview run created.", new { researchRunId });
        await _dbContext.SaveChangesAsync(cancellationToken);

        await EnqueueAsync(run, userId, cancellationToken);
        return MapSummary(run);
    }

    public async Task<AgentRunSummaryResponse> CreateDraftRevisionAsync(
        Guid userId,
        Guid criticReviewRunId,
        CancellationToken cancellationToken = default)
    {
        if (_workflowAdminService is not null) await _workflowAdminService.EnsureEnabledAsync(AgentWorkflowTypes.DraftRevision, cancellationToken);
        var provider = GetWorkflowProvider(AgentWorkflowTypes.DraftRevision);
        var run = provider.CreateRun(userId, criticReviewRunId);
        await SnapshotExecutionPoliciesAsync(run, cancellationToken);
        _dbContext.AgentRuns.Add(run);
        AddEvent(run, null, AgentEventTypes.RunCreated, "DraftRevision run created.", new { criticReviewRunId });
        await _dbContext.SaveChangesAsync(cancellationToken);

        await EnqueueAsync(run, userId, cancellationToken);
        return MapSummary(run);
    }

    public async Task<AgentRunSummaryResponse> CreateResearchQualityReviewAsync(
        Guid userId,
        Guid researchRunId,
        CancellationToken cancellationToken = default)
    {
        if (_workflowAdminService is not null) await _workflowAdminService.EnsureEnabledAsync(AgentWorkflowTypes.ResearchQualityReview, cancellationToken);
        var provider = GetWorkflowProvider(AgentWorkflowTypes.ResearchQualityReview);
        var run = provider.CreateRun(userId, researchRunId);
        await SnapshotExecutionPoliciesAsync(run, cancellationToken);
        _dbContext.AgentRuns.Add(run);
        AddEvent(run, null, AgentEventTypes.RunCreated, "ResearchQualityReview run created.", new { researchRunId });
        await _dbContext.SaveChangesAsync(cancellationToken);

        await EnqueueAsync(run, userId, cancellationToken);
        return MapSummary(run);
    }

    public async Task<AgentRunSummaryResponse> CreateEvidenceRemediationAsync(
        Guid userId,
        Guid criticReviewRunId,
        CancellationToken cancellationToken = default)
    {
        if (_workflowAdminService is not null) await _workflowAdminService.EnsureEnabledAsync(AgentWorkflowTypes.EvidenceRemediation, cancellationToken);
        var provider = GetWorkflowProvider(AgentWorkflowTypes.EvidenceRemediation);
        var run = provider.CreateRun(userId, criticReviewRunId);
        await SnapshotExecutionPoliciesAsync(run, cancellationToken);
        _dbContext.AgentRuns.Add(run);
        AddEvent(run, null, AgentEventTypes.RunCreated, "EvidenceRemediation run created.", new { criticReviewRunId });
        await _dbContext.SaveChangesAsync(cancellationToken);
        await EnqueueAsync(run, userId, cancellationToken);
        return MapSummary(run);
    }

    public async Task<AgentRunSummaryResponse> CreateEvidenceReanalysisAsync(
        Guid userId,
        Guid evidenceRemediationRunId,
        CancellationToken cancellationToken = default)
    {
        if (_workflowAdminService is not null) await _workflowAdminService.EnsureEnabledAsync(AgentWorkflowTypes.EvidenceReanalysis, cancellationToken);
        await EvidenceReanalysisSourceValidator.ValidateAsync(_dbContext, userId, evidenceRemediationRunId, cancellationToken);
        var provider = GetWorkflowProvider(AgentWorkflowTypes.EvidenceReanalysis);
        var run = provider.CreateRun(userId, evidenceRemediationRunId);
        await SnapshotExecutionPoliciesAsync(run, cancellationToken);
        _dbContext.AgentRuns.Add(run);
        AddEvent(run, null, AgentEventTypes.RunCreated, "EvidenceReanalysis run created.", new { evidenceRemediationRunId });
        await _dbContext.SaveChangesAsync(cancellationToken);
        await EnqueueAsync(run, userId, cancellationToken);
        return MapSummary(run);
    }

    public async Task<AgentRunSummaryResponse> CreatePortfolioDiagnosisAsync(
        Guid userId, Guid portfolioId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        if (_workflowAdminService is not null) await _workflowAdminService.EnsureEnabledAsync(AgentWorkflowTypes.PortfolioDiagnosis, cancellationToken);
        var isOwner = await _dbContext.Portfolios.AnyAsync(
            x => x.Id == portfolioId && x.OwnerUserId == userId && x.IsActive,
            cancellationToken);
        if (!isOwner) throw new InvalidOperationException("Portfolio was not found.");
        var provider = _portfolioDiagnosisProvider ?? throw new InvalidOperationException("PortfolioDiagnosis workflow provider is not registered.");
        var end = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var start = from ?? end.AddMonths(-1);
        if (start >= end) throw new InvalidOperationException("Diagnosis start date must be before end date.");
        var run = provider.CreateRun(userId, portfolioId, start, end);
        await SnapshotExecutionPoliciesAsync(run, cancellationToken);
        _dbContext.AgentRuns.Add(run);
        AddEvent(run, null, AgentEventTypes.RunCreated, "PortfolioDiagnosis run created.", new { portfolioId, from = start, to = end });
        await _dbContext.SaveChangesAsync(cancellationToken);
        await EnqueueAsync(run, userId, cancellationToken);
        return MapSummary(run);
    }

    public async Task<IReadOnlyList<AgentRunSummaryResponse>> ListAsync(
        Guid? userId,
        int limit = 50,
        string? workflowType = null,
        string? status = null,
        Guid? researchRunId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.AgentRuns.AsNoTracking();
        if (userId.HasValue)
            query = query.Where(x => x.UserId == userId.Value);
        if (!string.IsNullOrWhiteSpace(workflowType))
            query = query.Where(x => x.WorkflowType == workflowType.Trim());
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status.Trim());

        var runs = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(Math.Clamp(limit, 100, 1000))
            .ToListAsync(cancellationToken);

        if (researchRunId.HasValue)
        {
            var rid = researchRunId.Value.ToString("D");
            runs = runs.Where(x => x.InputJson.Contains($"\"researchRunId\":\"{rid}\"")).ToList();
        }

        return runs
            .Take(Math.Clamp(limit, 1, 100))
            .Select(x => MapSummary(x))
            .ToList();
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
                x.Id, x.NodeKey, x.TemplateNodeKey, x.Iteration, x.NodeType, x.Status, x.InputJson, x.OutputJson,
                x.ErrorMessage, x.ErrorCode, x.ErrorCategory, x.ErrorRetryable,
                x.InputBlackboardVersion, x.OutputBlackboardVersion, x.ProducedBlackboardKeys,
                x.BlackboardSnapshotJson,
                x.InputTokens, x.OutputTokens, x.EstimatedCostUsd,
                x.StartedAtUtc, x.CompletedAtUtc, x.DurationMs))
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

        _runStateMachine.ResetForRetry(run);
        run.ErrorMessage = null;
        run.OutputJson = null;
        var provider = GetWorkflowProvider(run.WorkflowType);
        run.BlackboardJson = provider.CreateInitialBlackboardJson(GetSourceRunId(run.InputJson));
        run.StartedAtUtc = null;
        run.CompletedAtUtc = null;
        var plannedArguments = GetPlannedArguments(run.WorkflowDefinitionJson);
        foreach (var node in run.Nodes)
        {
            _nodeStateMachine.ResetForRetry(node);
            node.InputJson = plannedArguments.TryGetValue(node.NodeKey, out var arguments) ? arguments : null;
            node.OutputJson = null;
            node.ErrorMessage = null;
            node.StartedAtUtc = null;
            node.CompletedAtUtc = null;
            node.DurationMs = null;
        }
        AddEvent(run, null, AgentEventTypes.SupervisorDecision, "Retry requested; run reset to Pending.", new { retry = true });
        await _dbContext.SaveChangesAsync(cancellationToken);

        await EnqueueAsync(run, userId, cancellationToken);
        return MapSummary(run);
    }

    internal static IReadOnlyDictionary<string, string> GetPlannedArguments(string definitionJson)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var nodes = JsonNode.Parse(definitionJson)?["nodes"]?.AsArray();
            if (nodes is null) return result;
            foreach (var node in nodes.OfType<JsonObject>())
            {
                var key = node["id"]?.GetValue<string>(); var arguments = node["plannedArguments"];
                if (!string.IsNullOrWhiteSpace(key) && arguments is not null) result[key] = arguments.ToJsonString(AgentNodeJson.SerializerOptions);
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException) { }
        return result;
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

        _runStateMachine.Transition(run, AgentRunStatuses.Cancelled);
        run.CompletedAtUtc = DateTime.UtcNow;
        AddEvent(run, null, AgentEventTypes.RunCancelled, "Run cancelled by user.", null);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapSummary(run);
    }

    private Task EnqueueAsync(AgentRun run, Guid userId, CancellationToken cancellationToken) =>
        _agentRunQueue.EnqueueAsync(
            new AgentRunQueueMessage(run.Id, userId, run.WorkflowType, DateTime.UtcNow),
            cancellationToken);

    private async Task SnapshotExecutionPoliciesAsync(AgentRun run, CancellationToken cancellationToken)
    {
        var root = JsonNode.Parse(run.WorkflowDefinitionJson)!.AsObject();
        var nodeTypes = root["nodes"]!.AsArray()
            .OfType<JsonObject>()
            .Select(x => x["type"]!.GetValue<string>())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var policies = _workflowAdminService is null
            ? nodeTypes.ToDictionary(x => x, x => _catalog.GetNode(x).DefaultPolicy)
            : await _workflowAdminService.GetPoliciesAsync(nodeTypes, cancellationToken);
        foreach (var node in root["nodes"]!.AsArray().OfType<JsonObject>())
        {
            var type = node["type"]!.GetValue<string>();
            var p = policies[type];
            node["executionPolicy"] = new JsonObject { ["timeoutSeconds"] = p.TimeoutSeconds, ["maxRetryCount"] = p.MaxRetryCount };
            node["contract"] = JsonSerializer.SerializeToNode(_catalog.GetNode(type).Contract, AgentNodeJson.SerializerOptions);
        }
        run.WorkflowDefinitionJson = root.ToJsonString(AgentNodeJson.SerializerOptions);
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
        if (root.TryGetProperty("portfolioId", out var portfolioId)) return portfolioId.GetGuid();

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
        run.ErrorMessage,
        run.TotalInputTokens,
        run.TotalOutputTokens,
        run.TotalEstimatedCostUsd);

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, SerializerOptions);
}
