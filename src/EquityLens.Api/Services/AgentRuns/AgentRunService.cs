using System.Text.Json;
using EquityLens.Api.Common;
using EquityLens.Api.Contracts.AgentRun;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.CurrentUser;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.AgentRuns;

public sealed class AgentRunService : IAgentRunService
{
    private readonly EquityLensDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public AgentRunService(EquityLensDbContext db, ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public async Task<Result<AgentRunCreatedResponse>> CreateCriticReviewAsync(
        CreateCriticReviewRequest request, CancellationToken ct)
    {
        if (request.ResearchRunId == Guid.Empty)
            return Result<AgentRunCreatedResponse>.Failure("agent_run.research_run_id_required", "researchRunId is required.");

        var workflowDefinition = CriticReviewWorkflow.Definition;
        var blackboard = CriticReviewWorkflow.InitialBlackboard(request.ResearchRunId);

        var run = new AgentRun
        {
            UserId = _currentUser.UserId,
            WorkflowType = "CriticReview",
            AgentType = "CriticAgent",
            Status = "Pending",
            InputJson = JsonSerializer.Serialize(new { researchRunId = request.ResearchRunId }),
            BlackboardJson = JsonSerializer.Serialize(blackboard),
            WorkflowDefinitionJson = JsonSerializer.Serialize(workflowDefinition)
        };

        _db.AgentRuns.Add(run);
        await _db.SaveChangesAsync(ct);

        return Result<AgentRunCreatedResponse>.Success(
            new AgentRunCreatedResponse(run.Id, run.Status));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentRunListItemResponse>> ListAsync(
        int limit, string? workflowType, string? status, CancellationToken ct)
    {
        var query = _db.AgentRuns.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(workflowType))
            query = query.Where(r => r.WorkflowType == workflowType);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(r => r.Status == status);

        var runs = await query
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(Math.Clamp(limit, 1, 100))
            .Select(r => new AgentRunListItemResponse(
                r.Id,
                r.WorkflowType,
                r.AgentType,
                r.Status,
                r.ErrorMessage,
                r.CreatedAtUtc,
                r.StartedAtUtc,
                r.CompletedAtUtc,
                r.Nodes.Count,
                r.Events.Count,
                r.ToolCalls.Count))
            .ToListAsync(ct);

        return runs;
    }

    /// <inheritdoc />
    public async Task<Result<AgentRunDetailResponse>> GetDetailAsync(Guid runId, CancellationToken ct)
    {
        var run = await _db.AgentRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == runId, ct);

        if (run is null)
            return Result<AgentRunDetailResponse>.Failure("agent_run.not_found", "Agent run not found.");

        var nodes = await _db.AgentRunNodes
            .AsNoTracking()
            .Where(n => n.AgentRunId == runId)
            .OrderBy(n => n.StartedAtUtc)
            .Select(n => new AgentRunNodeDto(
                n.Id, n.NodeKey, n.NodeType, n.Status,
                n.InputJson != null ? JsonDocument.Parse(n.InputJson).RootElement : null,
                n.OutputJson != null ? JsonDocument.Parse(n.OutputJson).RootElement : null,
                n.ErrorMessage, n.StartedAtUtc, n.CompletedAtUtc, n.DurationMs))
            .ToListAsync(ct);

        var events = await _db.AgentRunEvents
            .AsNoTracking()
            .Where(e => e.AgentRunId == runId)
            .OrderBy(e => e.CreatedAtUtc)
            .Select(e => new AgentRunEventDto(
                e.Id, e.AgentRunNodeId, e.EventType, e.Message,
                e.PayloadJson != null ? JsonDocument.Parse(e.PayloadJson).RootElement : null,
                e.CreatedAtUtc))
            .ToListAsync(ct);

        var toolCalls = await _db.AgentToolCalls
            .AsNoTracking()
            .Where(t => t.AgentRunId == runId)
            .OrderBy(t => t.StartedAtUtc)
            .Select(t => new AgentToolCallDto(
                t.Id, t.AgentRunNodeId, t.ToolName, t.Status,
                JsonDocument.Parse(t.ArgumentsJson).RootElement,
                t.ResultPreview,
                t.ResultJson != null ? JsonDocument.Parse(t.ResultJson).RootElement : null,
                t.ErrorMessage, t.StartedAtUtc, t.CompletedAtUtc, t.DurationMs))
            .ToListAsync(ct);

        return Result<AgentRunDetailResponse>.Success(new AgentRunDetailResponse(
            run.Id,
            run.WorkflowType,
            run.AgentType,
            run.Status,
            JsonDocument.Parse(run.InputJson).RootElement,
            run.OutputJson != null ? JsonDocument.Parse(run.OutputJson).RootElement : null,
            JsonDocument.Parse(run.BlackboardJson).RootElement,
            JsonDocument.Parse(run.WorkflowDefinitionJson).RootElement,
            run.ErrorMessage,
            run.CreatedAtUtc,
            run.StartedAtUtc,
            run.CompletedAtUtc,
            nodes, events, toolCalls));
    }

    /// <inheritdoc />
    public async Task<Result<AgentRunCreatedResponse>> RetryAsync(Guid runId, CancellationToken ct)
    {
        var run = await _db.AgentRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null)
            return Result<AgentRunCreatedResponse>.Failure("agent_run.not_found", "Agent run not found.");

        if (run.Status != "Failed")
            return Result<AgentRunCreatedResponse>.Failure("agent_run.cannot_retry", "Only failed runs can be retried.");

        // Clear previous execution data
        var nodes = await _db.AgentRunNodes.Where(n => n.AgentRunId == runId).ToListAsync(ct);
        _db.AgentRunNodes.RemoveRange(nodes);

        var events = await _db.AgentRunEvents.Where(e => e.AgentRunId == runId).ToListAsync(ct);
        _db.AgentRunEvents.RemoveRange(events);

        var toolCalls = await _db.AgentToolCalls.Where(t => t.AgentRunId == runId).ToListAsync(ct);
        _db.AgentToolCalls.RemoveRange(toolCalls);

        // Reset run state
        run.Status = "Pending";
        run.OutputJson = null;
        run.ErrorMessage = null;
        run.StartedAtUtc = null;
        run.CompletedAtUtc = null;

        var inputDoc = JsonDocument.Parse(run.InputJson);
        var researchRunId = Guid.Parse(inputDoc.RootElement.GetProperty("researchRunId").GetString()!);
        run.BlackboardJson = JsonSerializer.Serialize(
            CriticReviewWorkflow.InitialBlackboard(researchRunId));

        await _db.SaveChangesAsync(ct);

        return Result<AgentRunCreatedResponse>.Success(
            new AgentRunCreatedResponse(run.Id, run.Status));
    }

    /// <inheritdoc />
    public async Task<Result<bool>> CancelAsync(Guid runId, CancellationToken ct)
    {
        var run = await _db.AgentRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null)
            return Result<bool>.Failure("agent_run.not_found", "Agent run not found.");

        if (run.Status is "Succeeded" or "Failed" or "Cancelled")
            return Result<bool>.Failure("agent_run.cannot_cancel", "Run is already in a terminal state.");

        run.Status = "Cancelled";
        run.CompletedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }
}
