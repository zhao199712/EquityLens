using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Observability;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.Agents;

public sealed class AgentRunExecutor : IAgentRunExecutor
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly EquityLensDbContext _dbContext;
    private readonly IWorkflowGraphTopologyService _topology;
    private readonly IAgentRunGraphValidator _runGraphValidator;
    private readonly IAgentRunStateMachine _runStateMachine;
    private readonly IAgentNodeRunner _nodeRunner;
    private readonly IAgentDynamicPlanCoordinator _dynamicPlans;
    private readonly ILogger<AgentRunExecutor> _logger;
    private readonly IAgentNodeStateMachine _nodeStateMachine;
    private readonly AgentWorkflowPolicyReader _policies;
    private readonly TimeProvider _clock;
    private readonly AgentRunLeaseHeartbeat? _heartbeat;

    public AgentRunExecutor(
        EquityLensDbContext dbContext,
        IWorkflowGraphTopologyService topology,
        IAgentRunGraphValidator runGraphValidator,
        IAgentRunStateMachine runStateMachine,
        IAgentNodeRunner nodeRunner,
        IAgentDynamicPlanCoordinator dynamicPlans,
        ILogger<AgentRunExecutor> logger,
        IAgentNodeStateMachine? nodeStateMachine = null,
        AgentWorkflowPolicyReader? policies = null,
        TimeProvider? clock = null,
        AgentRunLeaseHeartbeat? heartbeat = null)
    {
        _dbContext = dbContext;
        _topology = topology;
        _runGraphValidator = runGraphValidator;
        _runStateMachine = runStateMachine;
        _nodeRunner = nodeRunner;
        _dynamicPlans = dynamicPlans;
        _logger = logger;
        _nodeStateMachine = nodeStateMachine ?? new AgentNodeStateMachine();
        _policies = policies ?? new AgentWorkflowPolicyReader();
        _clock = clock ?? TimeProvider.System;
        _heartbeat = heartbeat;
    }

    public async Task<AgentRunExecutionOutcome> ExecuteAsync(Guid runId, Guid userId, CancellationToken cancellationToken = default)
    {
        var run = await _dbContext.AgentRuns
            .Include(x => x.Nodes)
            .Include(x => x.Events)
            .Include(x => x.ToolCalls)
            .Include(x => x.Approvals)
            .FirstAsync(x => x.Id == runId && x.UserId == userId, cancellationToken);

        if (run.Status is AgentRunStatuses.Succeeded or AgentRunStatuses.Failed or AgentRunStatuses.Cancelled or AgentRunStatuses.WaitingForFeedback or AgentRunStatuses.WaitingForApproval)
        {
            _logger.LogInformation(
                "Skipping agent run {AgentRunId} because status is {Status}.",
                run.Id,
                run.Status);
            return AgentRunExecutionOutcome.SkippedTerminalOrWaiting;
        }

        using var activity = EquityLensTelemetry.ActivitySource.StartActivity("agent.run.execute");
        activity?.SetTag("agent.run.id", run.Id);
        activity?.SetTag("workflow.type", run.WorkflowType);
        activity?.SetTag("agent.type", run.AgentType);

        using var stopHeartbeat = new CancellationTokenSource();
        using var leaseLost = new CancellationTokenSource();
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, leaseLost.Token);
        var callerCancellation = cancellationToken;
        cancellationToken = execution.Token;
        Task heartbeatTask = Task.CompletedTask;
        try
        {
            var leaseOwner = $"{Environment.MachineName}:{Guid.NewGuid():N}";
            if (run.LeaseExpiresAtUtc > _clock.GetUtcNow().UtcDateTime && !string.IsNullOrWhiteSpace(run.LeaseOwner)) return AgentRunExecutionOutcome.LeaseHeldByOther;
            run.LeaseOwner = leaseOwner; run.LeaseExpiresAtUtc = _clock.GetUtcNow().UtcDateTime.AddMinutes(5); run.OrchestrationVersion++; run.LeaseVersion++;
            if (run.Status == AgentRunStatuses.Pending)
            {
                _runStateMachine.Transition(run, AgentRunStatuses.Running);
                run.StartedAtUtc ??= _clock.GetUtcNow().UtcDateTime;
                if (AgentWorkflowTypes.OwnsResearchArtifact(run.WorkflowType) && run.ResearchRunId is Guid researchRunId)
                {
                    var artifact = await _dbContext.ResearchRuns.SingleAsync(x => x.Id == researchRunId, cancellationToken);
                    artifact.Status = "Running";
                }
                AddEvent(run, null, AgentEventTypes.RunStarted, $"{run.WorkflowType} run started.", null);
            }
            try
            {
                await using var claim = _dbContext.Database.IsRelational()
                    ? await _dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
                if (claim is not null)
                {
                    var now = _clock.GetUtcNow().UtcDateTime;
                    var previousVersion = run.LeaseVersion - 1;
                    var matched = await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                        UPDATE agent_run SET lease_version = lease_version
                        WHERE id = {run.Id} AND lease_version = {previousVersion}
                          AND (lease_owner IS NULL OR lease_expires_at_utc IS NULL OR lease_expires_at_utc <= {now})
                          AND status IN ('Pending', 'Running')
                        """, cancellationToken);
                    if (matched != 1)
                    {
                        _dbContext.ChangeTracker.Clear();
                        return AgentRunExecutionOutcome.LeaseHeldByOther;
                    }
                }
                await _dbContext.SaveChangesAsync(cancellationToken);
                if (claim is not null) await claim.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                _dbContext.ChangeTracker.Clear();
                _logger.LogInformation("Agent run {AgentRunId} lease was claimed by another worker.", run.Id);
                return AgentRunExecutionOutcome.LeaseHeldByOther;
            }
            _dbContext.AgentWriteFence = new AgentRunWriteFence(run.Id, leaseOwner, run.LeaseVersion, _clock);
            heartbeatTask = _heartbeat?.RunAsync(run.Id, leaseOwner, run.LeaseVersion, leaseLost, stopHeartbeat.Token) ?? Task.CompletedTask;
            RecoverInterruptedNodes(run);

            const int maxBatchNodes = 6;
            var batchTimer = Stopwatch.StartNew();
            AgentRunNode? lastExecutedNode = null;
            for (var batchCount = 0; batchCount < maxBatchNodes && batchTimer.Elapsed < TimeSpan.FromSeconds(30); batchCount++)
            {
                var executionOrder = _topology.GetExecutionOrder(run.WorkflowDefinitionJson);
                _runGraphValidator.Validate(run, executionOrder);
                var nodeKey = GetNextReadyNode(run, executionOrder);
                if (nodeKey is null) break;
                if (!await _nodeRunner.ExecuteAsync(run, nodeKey, AddEvent, cancellationToken)) return AgentRunExecutionOutcome.Executed;
                lastExecutedNode = run.Nodes.Single(x => x.NodeKey == nodeKey);
                run.OrchestrationVersion++;
                if (run.Nodes.All(x => x.Status is AgentNodeStatuses.Succeeded or AgentNodeStatuses.Skipped))
                {
                    var graphAppended = await _dynamicPlans.TryAdvanceAsync(run, AddEvent, cancellationToken);
                    if (!graphAppended) CompleteRun(run);
                    run.LeaseOwner = null; run.LeaseExpiresAtUtc = null;
                    await SaveOrchestrationChangesAsync(graphAppended, cancellationToken);
                    return AgentRunExecutionOutcome.Executed;
                }
            }

            var executionOrderAfterBatch = _topology.GetExecutionOrder(run.WorkflowDefinitionJson);
            _runGraphValidator.Validate(run, executionOrderAfterBatch);
            var nextNode = GetNextReadyNode(run, executionOrderAfterBatch);
            if (nextNode is not null && lastExecutedNode is not null)
            {
                AddWakeOutbox(run, lastExecutedNode);
                run.LeaseOwner = null; run.LeaseExpiresAtUtc = null;
                AddEvent(run, lastExecutedNode, AgentEventTypes.SchedulerDecision, "Node batch yielded back to the queue.", new { executedNodes = run.Nodes.Count(x => x.Status == AgentNodeStatuses.Succeeded), maxBatchNodes, elapsedMs = batchTimer.ElapsedMilliseconds });
                await SaveOrchestrationChangesAsync(false, cancellationToken);
                return AgentRunExecutionOutcome.Executed;
            }

            var appended = false;
            if (run.Nodes.All(x => x.Status is AgentNodeStatuses.Succeeded or AgentNodeStatuses.Skipped))
            {
                appended = await _dynamicPlans.TryAdvanceAsync(run, AddEvent, cancellationToken);
                if (!appended) CompleteRun(run);
            }
            else if (run.Nodes.Any(x => x.Status == AgentNodeStatuses.WaitingForFeedback))
            {
                _runStateMachine.Transition(run, AgentRunStatuses.WaitingForFeedback);
                AddEvent(run, null, AgentEventTypes.RunWaitingForFeedback, "Run paused waiting for human approval.", new { waitingNodes = run.Nodes.Count(x => x.Status == AgentNodeStatuses.WaitingForFeedback) });
                run.LeaseOwner = null; run.LeaseExpiresAtUtc = null;
                await SaveOrchestrationChangesAsync(false, cancellationToken);
                return AgentRunExecutionOutcome.Executed;
            }
            else throw new InvalidOperationException("Workflow has no ready node but is not complete.");
            run.LeaseOwner = null; run.LeaseExpiresAtUtc = null;
            await SaveOrchestrationChangesAsync(appended, cancellationToken);
        }
        catch (Exception exception) when (exception is AgentRunLeaseLostException or DbUpdateConcurrencyException || leaseLost.IsCancellationRequested)
        {
            _dbContext.ChangeTracker.Clear();
            _logger.LogInformation("Agent run {AgentRunId} stopped after losing its lease.", runId);
            EquityLensTelemetry.AgentLeaseLost.Add(1);
            return AgentRunExecutionOutcome.LeaseLostDuringExecution;
        }
        catch (OperationCanceledException) when (callerCancellation.IsCancellationRequested)
        {
            _dbContext.ChangeTracker.Clear();
            throw;
        }
        catch (Exception exception)
        {
            EquityLensTelemetry.MarkError(activity, exception);
            _logger.LogError(exception, "Agent run {AgentRunId} failed", runId);
            if (run.WorkflowType is AgentWorkflowTypes.ResearchQualityReview or AgentWorkflowTypes.PortfolioDiagnosis)
            {
                var reasonCode = exception is AgentLoopPlanValidationException
                    ? AgentLoopStopReasons.PlanValidationFailed
                    : AgentLoopStopReasons.RunFailed;
                MarkLoopFailed(run, reasonCode, exception.Message);
                AddEvent(run, null, AgentEventTypes.LoopStopped, $"{run.WorkflowType} loop failed: {reasonCode}.", new { reasonCode, error = exception.Message });
            }
            if (run.Status != AgentRunStatuses.Failed)
            {
                _runStateMachine.Transition(run, AgentRunStatuses.Failed);
            }
            run.ErrorMessage = exception.Message;
            run.CompletedAtUtc = _clock.GetUtcNow().UtcDateTime;
            await UpdateResearchArtifactTerminalAsync(run, "Failed", cancellationToken);
            run.LeaseOwner = null; run.LeaseExpiresAtUtc = null;
            AddEvent(run, null, AgentEventTypes.RunFailed, $"{run.WorkflowType} run failed.", new { error = exception.Message });
            try { await _dbContext.SaveChangesAsync(cancellationToken); }
            catch (Exception writeError) when (writeError is AgentRunLeaseLostException or DbUpdateConcurrencyException)
            {
                _dbContext.ChangeTracker.Clear();
                EquityLensTelemetry.AgentLeaseLost.Add(1);
            return AgentRunExecutionOutcome.LeaseLostDuringExecution;
            }
        }
        finally
        {
            stopHeartbeat.Cancel();
            await heartbeatTask;
            _dbContext.AgentWriteFence = null;
        }
        return AgentRunExecutionOutcome.Executed;
    }

    private void RecoverInterruptedNodes(AgentRun run)
    {
        foreach (var node in run.Nodes.Where(x => x.Status is AgentNodeStatuses.Ready or AgentNodeStatuses.Queued or AgentNodeStatuses.Running))
        {
            var previousStatus = node.Status;
            node.InterruptionCount++;
            EquityLensTelemetry.AgentNodeInterrupted.Add(1);
            AddEvent(run, node, "NodeInterrupted", "Interrupted node recovered under a new lease.", new { previousStatus, node.InterruptionCount, run.LeaseVersion });
            var policy = _policies.GetExecutionPolicy(run.WorkflowDefinitionJson, node.NodeKey, node.NodeType);
            var definition = JsonNode.Parse(run.WorkflowDefinitionJson)?["nodes"]?.AsArray().OfType<JsonObject>()
                .SingleOrDefault(x => x["id"]?.GetValue<string>() == node.NodeKey);
            var unsafeToRepeat = previousStatus == AgentNodeStatuses.Running
                && definition?["contract"]?["isIdempotent"]?.GetValue<bool>() == false;
            // 執行過的非冪等節點必須人工處理；尚未執行的 Ready / Queued 可安全排程。
            if (unsafeToRepeat || node.InterruptionCount > Math.Max(1, policy.MaxRetryCount + 1))
            {
                if (node.Status != AgentNodeStatuses.Running)
                {
                    _nodeStateMachine.ResetForRetry(node);
                    _nodeStateMachine.Transition(node, AgentNodeStatuses.Ready);
                    _nodeStateMachine.Transition(node, AgentNodeStatuses.Running);
                }
                _nodeStateMachine.Transition(node, AgentNodeStatuses.Failed);
                node.ErrorCode = "interrupted";
                node.ErrorCategory = "Interrupted";
                node.ErrorRetryable = false;
                node.ErrorMessage = unsafeToRepeat ? "Interrupted non-idempotent node cannot be automatically repeated." : "Node interruption budget exhausted.";
                node.CompletedAtUtc = _clock.GetUtcNow().UtcDateTime;
                throw new AgentNodeException("interrupted", "Interrupted", node.ErrorMessage, false);
            }
            _nodeStateMachine.ResetForRetry(node);
            node.ErrorMessage = null;
            node.StartedAtUtc = null;
            node.CompletedAtUtc = null;
            node.OutputJson = null;
        }
    }

    private string? GetNextReadyNode(AgentRun run, IReadOnlyList<string> executionOrder)
    {
        using var definition = JsonDocument.Parse(run.WorkflowDefinitionJson);
        var nodeDefinitions = definition.RootElement.GetProperty("nodes").EnumerateArray().ToDictionary(x => x.GetProperty("id").GetString()!, StringComparer.Ordinal);
        var predecessors = executionOrder.ToDictionary(x => x, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var edge in definition.RootElement.GetProperty("edges").EnumerateArray())
        {
            var from = edge.GetProperty("from").GetString()!; var to = edge.GetProperty("to").GetString()!;
            predecessors[to].Add(from);
        }
        foreach (var key in executionOrder)
        {
            var node = run.Nodes.Single(x => x.NodeKey == key);
            if (node.Status != AgentNodeStatuses.Pending || !predecessors[key].All(p => run.Nodes.Single(x => x.NodeKey == p).Status is AgentNodeStatuses.Succeeded or AgentNodeStatuses.Skipped)) continue;
            if (nodeDefinitions[key].TryGetProperty("condition", out var condition) && condition.ValueKind == JsonValueKind.Object)
            {
                var path = condition.GetProperty("path").GetString()!; var expected = condition.GetProperty("equals").GetString();
                var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson); var actual = ResolveConditionValue(board, path);
                if (!string.Equals(actual, expected, StringComparison.Ordinal))
                {
                    _nodeStateMachine.Transition(node, AgentNodeStatuses.Skipped); node.CompletedAtUtc = _clock.GetUtcNow().UtcDateTime;
                    AddEvent(run, node, AgentEventTypes.SchedulerDecision, $"Node {key} skipped because its condition was false.", new { decision = "SkipNode", path, expected, actual });
                    continue;
                }
            }
            return key;
        }
        return null;
    }

    private static string? ResolveConditionValue(JsonObject board, string path)
    {
        JsonNode? current = board;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current is not JsonObject obj) return null;
            current = obj[segment];
        }
        return current?.GetValue<string>();
    }

    private void CompleteRun(AgentRun run)
    {
        _runStateMachine.Transition(run, AgentRunStatuses.Succeeded); run.CompletedAtUtc = _clock.GetUtcNow().UtcDateTime;
        if (AgentWorkflowTypes.OwnsResearchArtifact(run.WorkflowType) && run.ResearchRunId is Guid researchRunId)
        {
            var artifact = _dbContext.ResearchRuns.Local.SingleOrDefault(x => x.Id == researchRunId)
                ?? _dbContext.ResearchRuns.Single(x => x.Id == researchRunId);
            var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
            artifact.Answer = board[AgentBlackboardKeys.RevisedAnswer]?.GetValue<string>()
                ?? board[AgentBlackboardKeys.Answer]?.GetValue<string>() ?? artifact.Answer;
            artifact.Status = run.WorkflowType == AgentWorkflowTypes.ResearchAgent
                ? board["researchAgentFinalStatus"]?.GetValue<string>() ?? "InsufficientEvidence"
                : artifact.Answer.Contains("資料不足", StringComparison.OrdinalIgnoreCase) ? "InsufficientEvidence" : "Answered";
            artifact.LatencyMs = run.StartedAtUtc.HasValue ? (long)(_clock.GetUtcNow().UtcDateTime - run.StartedAtUtc.Value).TotalMilliseconds : 0;
        }
        AddEvent(run, null, AgentEventTypes.RunSucceeded, $"{run.WorkflowType} run succeeded.", null);
    }

    private async Task SaveOrchestrationChangesAsync(bool graphAppended, CancellationToken cancellationToken)
    {
        if (!graphAppended)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        // Appending a graph patch must not rewrite completed executions. Some providers
        // mark the existing relationship members as modified when new nodes are added;
        // keeping them unchanged also prevents an unrelated stale node row from rolling
        // back the otherwise atomic planner/tool-call/graph-patch transaction.
        _dbContext.ChangeTracker.DetectChanges();
        foreach (var entry in _dbContext.ChangeTracker.Entries<AgentRunNode>()
                     .Where(x => x.State == EntityState.Modified))
        {
            entry.OriginalValues.SetValues(entry.CurrentValues);
            entry.State = EntityState.Unchanged;
        }
        _dbContext.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _dbContext.ChangeTracker.AutoDetectChangesEnabled = true;
        }
    }

    private void AddWakeOutbox(AgentRun run, AgentRunNode cause)
    {
        var version = JsonNode.Parse(run.WorkflowDefinitionJson)?["version"]?.GetValue<int>() ?? 1;
        _dbContext.AgentRunWakeOutbox.Add(new AgentRunWakeOutbox { Id = Guid.NewGuid(), AgentRunId = run.Id, UserId = run.UserId, WorkflowType = run.WorkflowType, AgentRunNodeId = cause.Id, DefinitionVersion = version, OrchestrationVersion = run.OrchestrationVersion, CorrelationId = run.Id, CausationId = cause.Id });
    }

    private async Task UpdateResearchArtifactTerminalAsync(AgentRun run, string status, CancellationToken cancellationToken)
    {
        if (!AgentWorkflowTypes.OwnsResearchArtifact(run.WorkflowType) || run.ResearchRunId is not Guid researchRunId) return;
        var artifact = await _dbContext.ResearchRuns.SingleOrDefaultAsync(x => x.Id == researchRunId, cancellationToken);
        if (artifact is null) return;
        artifact.Status = status; artifact.ErrorMessage = run.ErrorMessage;
        artifact.LatencyMs = run.StartedAtUtc.HasValue ? (long)(_clock.GetUtcNow().UtcDateTime - run.StartedAtUtc.Value).TotalMilliseconds : 0;
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
            CreatedAtUtc = _clock.GetUtcNow().UtcDateTime
        });
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, SerializerOptions);

    private static void MarkLoopFailed(AgentRun run, string reasonCode, string message)
    {
        var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        var runtime = board[AgentBlackboardKeys.Runtime] as JsonObject ?? new JsonObject();
        runtime["status"] = "Failed";
        runtime["stopReason"] = reasonCode;
        runtime["lastReasonCode"] = reasonCode;
        runtime["failureMessage"] = message;
        board[AgentBlackboardKeys.Runtime] = runtime;
        run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);
    }
}
