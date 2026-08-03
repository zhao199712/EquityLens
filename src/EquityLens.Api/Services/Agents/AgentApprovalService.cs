using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Agents;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Observability;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.Agents;

public interface IAgentApprovalService
{
    Task<AgentApprovalResponse?> ApproveAsync(Guid runId, Guid approvalId, Guid actorUserId, bool isAdmin, DecideAgentApprovalRequest request, CancellationToken cancellationToken = default);
    Task<AgentApprovalResponse?> RejectAsync(Guid runId, Guid approvalId, Guid actorUserId, bool isAdmin, DecideAgentApprovalRequest request, CancellationToken cancellationToken = default);
}

public sealed class AgentApprovalService(
    EquityLensDbContext db,
    IAgentRunStateMachine runStateMachine,
    IAgentNodeStateMachine nodeStateMachine) : IAgentApprovalService
{
    public Task<AgentApprovalResponse?> ApproveAsync(Guid runId, Guid approvalId, Guid actorUserId, bool isAdmin, DecideAgentApprovalRequest request, CancellationToken cancellationToken = default) =>
        DecideAsync(runId, approvalId, actorUserId, isAdmin, request, approve: true, cancellationToken);

    public Task<AgentApprovalResponse?> RejectAsync(Guid runId, Guid approvalId, Guid actorUserId, bool isAdmin, DecideAgentApprovalRequest request, CancellationToken cancellationToken = default) =>
        DecideAsync(runId, approvalId, actorUserId, isAdmin, request, approve: false, cancellationToken);

    private async Task<AgentApprovalResponse?> DecideAsync(
        Guid runId,
        Guid approvalId,
        Guid actorUserId,
        bool isAdmin,
        DecideAgentApprovalRequest request,
        bool approve,
        CancellationToken cancellationToken)
    {
        if (request.RequestId == Guid.Empty)
            throw new AgentApprovalException("approval_request_id_required", "RequestId is required.");
        var comment = request.Comment?.Trim();
        if (!approve && string.IsNullOrWhiteSpace(comment))
            throw new AgentApprovalException("approval_rejection_comment_required", "Reject comment is required.");

        var approval = await db.AgentApprovalRequests
            .Include(x => x.Run)
            .Include(x => x.Node)
            .SingleOrDefaultAsync(x => x.Id == approvalId && x.AgentRunId == runId, cancellationToken);
        if (approval is null || (!isAdmin && approval.Run.UserId != actorUserId)) return null;

        if (approval.Status != AgentApprovalStatuses.Pending)
        {
            if (approval.ClientRequestId == request.RequestId) return Map(approval);
            throw new AgentApprovalException("approval_already_decided", "Approval request was already decided.");
        }

        var run = approval.Run;
        var node = approval.Node;
        if (run.Status != AgentRunStatuses.WaitingForApproval || node.Status != AgentNodeStatuses.WaitingForApproval)
            throw new AgentApprovalException("approval_state_conflict", "Agent run is no longer waiting for this approval.");

        var now = DateTime.UtcNow;
        approval.Status = approve ? AgentApprovalStatuses.Approved : AgentApprovalStatuses.Rejected;
        approval.DecidedAtUtc = now;
        approval.DecidedByUserId = actorUserId;
        approval.ClientRequestId = request.RequestId;
        approval.DecisionComment = comment;

        if (approve)
        {
            nodeStateMachine.Transition(node, AgentNodeStatuses.Pending);
            runStateMachine.Transition(run, AgentRunStatuses.Running);
            run.LeaseOwner = null;
            run.LeaseExpiresAtUtc = null;
            run.OrchestrationVersion++;
            AddEvent(run, node, AgentEventTypes.ApprovalApproved, "Human approval granted; run queued to resume.", new { approvalId, actorUserId, isAdmin, comment });
            AddWakeOutbox(run, node);
        }
        else
        {
            nodeStateMachine.Transition(node, AgentNodeStatuses.Cancelled);
            foreach (var pendingNode in await db.AgentRunNodes.Where(x => x.AgentRunId == run.Id && x.Id != node.Id).ToListAsync(cancellationToken))
            {
                if (pendingNode.Status is AgentNodeStatuses.Pending or AgentNodeStatuses.Ready or AgentNodeStatuses.Queued or AgentNodeStatuses.WaitingForApproval or AgentNodeStatuses.Running)
                    nodeStateMachine.Transition(pendingNode, AgentNodeStatuses.Cancelled);
            }
            runStateMachine.Transition(run, AgentRunStatuses.Cancelled);
            run.CompletedAtUtc = now;
            run.LeaseOwner = null;
            run.LeaseExpiresAtUtc = null;
            if ((run.WorkflowType is AgentWorkflowTypes.ResearchInvestigation or AgentWorkflowTypes.FeedbackRevision) && run.ResearchRunId is Guid researchRunId)
            {
                var researchRun = await db.ResearchRuns.SingleOrDefaultAsync(x => x.Id == researchRunId, cancellationToken);
                if (researchRun is not null) researchRun.Status = "Cancelled";
            }
            AddEvent(run, node, AgentEventTypes.ApprovalRejected, "Human approval rejected; run cancelled.", new { approvalId, actorUserId, isAdmin, comment });
            AddEvent(run, null, AgentEventTypes.RunCancelled, "Run cancelled because a required approval was rejected.", new { approvalId });
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            var current = await db.AgentApprovalRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == approvalId, cancellationToken);
            if (current?.ClientRequestId == request.RequestId) return Map(current);
            throw new AgentApprovalException("approval_already_decided", "Approval request was decided concurrently.");
        }

        var tags = new TagList
        {
            { "approval.decision", approve ? "approved" : "rejected" },
            { "workflow.type", run.WorkflowType },
            { "node.type", node.NodeType },
            { "side_effect.level", approval.SideEffectLevel }
        };
        EquityLensTelemetry.AgentApprovalDecisions.Add(1, tags);
        EquityLensTelemetry.AgentApprovalWaitDuration.Record((now - approval.RequestedAtUtc).TotalMilliseconds, tags);
        return Map(approval);
    }

    private void AddEvent(AgentRun run, AgentRunNode? node, string eventType, string message, object payload) =>
        db.AgentRunEvents.Add(new AgentRunEvent
        {
            Id = Guid.NewGuid(), AgentRunId = run.Id, AgentRunNodeId = node?.Id,
            EventType = eventType, Message = message, PayloadJson = JsonSerializer.Serialize(payload),
            CreatedAtUtc = DateTime.UtcNow
        });

    private void AddWakeOutbox(AgentRun run, AgentRunNode node)
    {
        var version = JsonNode.Parse(run.WorkflowDefinitionJson)?["version"]?.GetValue<int>() ?? 1;
        db.AgentRunWakeOutbox.Add(new AgentRunWakeOutbox
        {
            Id = Guid.NewGuid(), AgentRunId = run.Id, UserId = run.UserId, WorkflowType = run.WorkflowType,
            AgentRunNodeId = node.Id, DefinitionVersion = version, OrchestrationVersion = run.OrchestrationVersion,
            CorrelationId = run.Id, CausationId = node.Id,
            PublishedAtUtc = run.WorkflowType == E2EApprovalGateWorkflow.WorkflowType ? DateTime.UtcNow : null
        });
    }

    internal static AgentApprovalResponse Map(AgentApprovalRequest x) => new(
        x.Id, x.AgentRunId, x.AgentRunNodeId, x.NodeKey, x.NodeType, x.Status, x.SideEffectLevel,
        x.Reason, x.RequestedAtUtc, x.DecidedAtUtc, x.ConsumedAtUtc, x.DecidedByUserId,
        x.DecisionComment, x.ClientRequestId);
}

public sealed class AgentApprovalException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}
