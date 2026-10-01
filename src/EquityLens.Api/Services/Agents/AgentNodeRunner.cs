using System.Diagnostics;
using System.Text.Json;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Observability;

namespace EquityLens.Api.Services.Agents;

public interface IAgentNodeRunner
{
    Task<bool> ExecuteAsync(AgentRun run, string nodeKey, AgentRunEventWriter addEvent, CancellationToken cancellationToken);
}

/// <summary>執行單一節點，保留批准、重試、逾時與節點儲存邊界。</summary>
public sealed class AgentNodeRunner(
    EquityLensDbContext dbContext,
    IAgentRunStateMachine runStateMachine,
    IAgentNodeStateMachine nodeStateMachine,
    IEnumerable<IAgentNodeHandler> handlers,
    AgentWorkflowPolicyReader policies) : IAgentNodeRunner
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IReadOnlyDictionary<string, IAgentNodeHandler> _nodeHandlers =
        handlers.ToDictionary(x => x.NodeType, StringComparer.Ordinal);

    public async Task<bool> ExecuteAsync(AgentRun run, string nodeKey, AgentRunEventWriter addEvent, CancellationToken cancellationToken)
    {
        var node = run.Nodes.First(x => x.NodeKey == nodeKey);
        if (!await EnsureApprovalAsync(run, node, addEvent, cancellationToken)) return false;
        using var activity = EquityLensTelemetry.ActivitySource.StartActivity("agent.node.execute");
        activity?.SetTag("agent.run.id", run.Id);
        activity?.SetTag("agent.run.node.id", node.Id);
        activity?.SetTag("workflow.type", run.WorkflowType);
        activity?.SetTag("node.key", node.NodeKey);
        activity?.SetTag("node.type", node.NodeType);
        var policy = policies.GetExecutionPolicy(run.WorkflowDefinitionJson, node.NodeKey, node.NodeType);
        var decisionPayload = new { decision = "RunNode", nextNodeId = nodeKey, reason = "Previous dependencies are satisfied.", mode = "Deterministic", policy.TimeoutSeconds, policy.MaxRetryCount };
        addEvent(run, node, AgentEventTypes.SchedulerDecision, $"Scheduler selected {nodeKey}.", decisionPayload);
        for (var attempt = 0; attempt <= policy.MaxRetryCount; attempt++)
        {
            nodeStateMachine.Transition(node, AgentNodeStatuses.Ready);
            addEvent(run, node, AgentEventTypes.NodeReady, $"Node {nodeKey} is ready.", null);
            nodeStateMachine.Transition(node, AgentNodeStatuses.Queued);
            addEvent(run, node, AgentEventTypes.SchedulerDecision, $"Node {nodeKey} queued.", new { decision = "QueueNode", node.Iteration });
            nodeStateMachine.Transition(node, AgentNodeStatuses.Running);
            node.StartedAtUtc = DateTime.UtcNow;
            addEvent(run, node, AgentEventTypes.NodeStarted, $"Node {nodeKey} started.", new { attempt = attempt + 1, policy.TimeoutSeconds });
            await dbContext.SaveChangesAsync(cancellationToken);

            var stopwatch = Stopwatch.StartNew();
            var boardBefore = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
            node.InputBlackboardVersion = boardBefore["blackboardVersion"]?.GetValue<int>() ?? 0;
            try
            {
                if (!_nodeHandlers.TryGetValue(node.NodeType, out var handler))
                {
                    throw new InvalidOperationException($"Unsupported node type '{node.NodeType}'.");
                }

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(policy.TimeoutSeconds));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
                var context = new AgentNodeExecutionContext(dbContext, run, node, addEvent);
                await handler.ExecuteAsync(context, linked.Token);

                stopwatch.Stop();
                var boardAfter = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
                node.OutputBlackboardVersion = boardAfter["blackboardVersion"]?.GetValue<int>() ?? 0;
                node.ProducedBlackboardKeys = AgentNodeJson.DetectChangedKeys(boardBefore, boardAfter);
                AgentNodeJson.IncrementBlackboardVersion(boardAfter);
                run.BlackboardJson = boardAfter.ToJsonString(AgentNodeJson.SerializerOptions);
                if (context.AwaitingApproval)
                {
                    nodeStateMachine.Transition(node, AgentNodeStatuses.WaitingForFeedback);
                    runStateMachine.Transition(run, AgentRunStatuses.WaitingForFeedback);
                    run.LeaseOwner = null;
                    run.LeaseExpiresAtUtc = null;
                    node.DurationMs = stopwatch.ElapsedMilliseconds;
                    addEvent(run, node, AgentEventTypes.ApprovalRequested, $"Node {nodeKey} paused waiting for human approval.", null);
                    await dbContext.SaveChangesAsync(cancellationToken);
                    return false;
                }
                nodeStateMachine.Transition(node, AgentNodeStatuses.Succeeded);
                node.CompletedAtUtc = DateTime.UtcNow;
                node.DurationMs = stopwatch.ElapsedMilliseconds;
                if (run.EnableBlackboardSnapshots)
                {
                    node.BlackboardSnapshotJson = run.BlackboardJson;
                }
                run.TotalInputTokens += node.InputTokens ?? 0;
                run.TotalOutputTokens += node.OutputTokens ?? 0;
                run.TotalEstimatedCostUsd += node.EstimatedCostUsd ?? 0;
                addEvent(run, node, AgentEventTypes.NodeCompleted, $"Node {nodeKey} completed.", new { durationMs = node.DurationMs });
                await dbContext.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (AgentNodeException ex)
            {
                EquityLensTelemetry.MarkError(activity, ex);
                stopwatch.Stop();
                nodeStateMachine.Transition(node, AgentNodeStatuses.Failed);
                node.ErrorMessage = ex.Message;
                node.ErrorCode = ex.ErrorCode;
                node.ErrorCategory = ex.ErrorCategory;
                node.ErrorRetryable = ex.Retryable;
                node.CompletedAtUtc = DateTime.UtcNow;
                node.DurationMs = stopwatch.ElapsedMilliseconds;
                addEvent(run, node, AgentEventTypes.NodeFailed, $"Node {nodeKey} failed.", new { error = ex.Message, ex.ErrorCode, ex.ErrorCategory, ex.Retryable });
                if (attempt >= policy.MaxRetryCount || !ex.Retryable) throw;
                addEvent(run, node, AgentEventTypes.SupervisorDecision, $"Retrying {nodeKey}.", new { attempt = attempt + 2, policy.MaxRetryCount });
                nodeStateMachine.ResetForRetry(node);
                node.ErrorMessage = null; node.ErrorCode = null; node.ErrorCategory = null; node.ErrorRetryable = null;
                node.InputBlackboardVersion = null; node.OutputBlackboardVersion = null; node.ProducedBlackboardKeys = null;
                node.StartedAtUtc = null; node.CompletedAtUtc = null; node.DurationMs = null;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                EquityLensTelemetry.MarkError(activity, new TimeoutException());
                stopwatch.Stop();
                nodeStateMachine.Transition(node, AgentNodeStatuses.Failed);
                node.ErrorMessage = $"Node timed out after {policy.TimeoutSeconds} seconds.";
                node.ErrorCode = "timeout";
                node.ErrorCategory = AgentNodeErrorCategories.TimedOut;
                node.ErrorRetryable = false;
                node.CompletedAtUtc = DateTime.UtcNow;
                node.DurationMs = stopwatch.ElapsedMilliseconds;
                addEvent(run, node, AgentEventTypes.NodeFailed, $"Node {nodeKey} timed out.", new { policy.TimeoutSeconds });
                throw;
            }
            catch (Exception exception)
            {
                EquityLensTelemetry.MarkError(activity, exception);
                stopwatch.Stop();
                nodeStateMachine.Transition(node, AgentNodeStatuses.Failed);
                node.ErrorMessage = exception.Message;
                node.ErrorCode = "unhandled_exception";
                node.ErrorCategory = AgentNodeErrorCategories.PermanentFailure;
                node.ErrorRetryable = false;
                node.CompletedAtUtc = DateTime.UtcNow;
                node.DurationMs = stopwatch.ElapsedMilliseconds;
                addEvent(run, node, AgentEventTypes.NodeFailed, $"Node {nodeKey} failed.", new { error = exception.Message });
                if (attempt >= policy.MaxRetryCount) throw;
                addEvent(run, node, AgentEventTypes.SupervisorDecision, $"Retrying {nodeKey}.", new { attempt = attempt + 2, policy.MaxRetryCount });
                nodeStateMachine.ResetForRetry(node);
                node.ErrorMessage = null; node.ErrorCode = null; node.ErrorCategory = null; node.ErrorRetryable = null;
                node.InputBlackboardVersion = null; node.OutputBlackboardVersion = null; node.ProducedBlackboardKeys = null;
                node.StartedAtUtc = null; node.CompletedAtUtc = null; node.DurationMs = null;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        return true;
    }

    private async Task<bool> EnsureApprovalAsync(AgentRun run, AgentRunNode node, AgentRunEventWriter addEvent, CancellationToken cancellationToken)
    {
        var policy = policies.GetApprovalPolicy(run.WorkflowDefinitionJson, node.NodeKey, node.NodeType);
        if (!policy.RequiresHumanApproval) return true;

        var current = run.Approvals
            .Where(x => x.AgentRunNodeId == node.Id && x.ExecutionAttempt == run.ExecutionAttempt)
            .OrderByDescending(x => x.RequestedAtUtc)
            .FirstOrDefault();
        if (current?.Status == AgentApprovalStatuses.Approved)
        {
            current.ConsumedAtUtc ??= DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        if (current?.Status == AgentApprovalStatuses.Pending)
        {
            run.LeaseOwner = null;
            run.LeaseExpiresAtUtc = null;
            await dbContext.SaveChangesAsync(cancellationToken);
            return false;
        }

        var request = new AgentApprovalRequest
        {
            Id = Guid.NewGuid(),
            AgentRunId = run.Id,
            AgentRunNodeId = node.Id,
            NodeKey = node.NodeKey,
            NodeType = node.NodeType,
            Status = AgentApprovalStatuses.Pending,
            SideEffectLevel = policy.SideEffectLevel,
            Reason = policy.Reason,
            PolicySnapshotJson = JsonSerializer.Serialize(policy, SerializerOptions),
            ExecutionAttempt = run.ExecutionAttempt,
            RequestedAtUtc = DateTime.UtcNow,
            Run = run,
            Node = node
        };
        dbContext.AgentApprovalRequests.Add(request);
        nodeStateMachine.Transition(node, AgentNodeStatuses.WaitingForApproval);
        runStateMachine.Transition(run, AgentRunStatuses.WaitingForApproval);
        run.LeaseOwner = null;
        run.LeaseExpiresAtUtc = null;
        run.OrchestrationVersion++;
        addEvent(run, node, AgentEventTypes.ApprovalRequested, policy.Reason, new
        {
            approvalId = request.Id,
            node.NodeKey,
            node.NodeType,
            policy.SideEffectLevel,
            run.ExecutionAttempt
        });
        EquityLensTelemetry.AgentApprovalDecisions.Add(1,
            new KeyValuePair<string, object?>("approval.decision", "requested"),
            new KeyValuePair<string, object?>("workflow.type", run.WorkflowType),
            new KeyValuePair<string, object?>("node.type", node.NodeType),
            new KeyValuePair<string, object?>("side_effect.level", policy.SideEffectLevel));
        await dbContext.SaveChangesAsync(cancellationToken);
        return false;
    }
}
