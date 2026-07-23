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
    private readonly IAgentNodeStateMachine _nodeStateMachine;
    private readonly IReadOnlyDictionary<string, IAgentNodeHandler> _nodeHandlers;
    private readonly ILogger<AgentRunExecutor> _logger;
    private readonly IAgentWorkflowCatalog? _catalog;
    private readonly IAgentWorkflowPlanner? _dynamicPlanner;
    private readonly IDynamicPlanValidator? _dynamicPlanValidator;
    private readonly IGraphMaterializer? _graphMaterializer;
    private readonly IWorkflowSkillCatalog? _skills;
    private readonly INodeCapabilityRegistry? _capabilities;

    public AgentRunExecutor(
        EquityLensDbContext dbContext,
        IWorkflowGraphTopologyService topology,
        IAgentRunGraphValidator runGraphValidator,
        IAgentRunStateMachine runStateMachine,
        IAgentNodeStateMachine nodeStateMachine,
        IEnumerable<IAgentNodeHandler> nodeHandlers,
        ILogger<AgentRunExecutor> logger,
        IAgentWorkflowCatalog? catalog = null,
        IAgentWorkflowPlanner? dynamicPlanner = null,
        IDynamicPlanValidator? dynamicPlanValidator = null,
        IGraphMaterializer? graphMaterializer = null,
        IWorkflowSkillCatalog? skills = null,
        INodeCapabilityRegistry? capabilities = null)
    {
        _dbContext = dbContext;
        _topology = topology;
        _runGraphValidator = runGraphValidator;
        _runStateMachine = runStateMachine;
        _nodeStateMachine = nodeStateMachine;
        _nodeHandlers = nodeHandlers.ToDictionary(x => x.NodeType, StringComparer.Ordinal);
        _logger = logger;
        _catalog = catalog;
        _dynamicPlanner = dynamicPlanner;
        _dynamicPlanValidator = dynamicPlanValidator;
        _graphMaterializer = graphMaterializer;
        _skills = skills;
        _capabilities = capabilities;
    }

    public async Task ExecuteAsync(Guid runId, Guid userId, CancellationToken cancellationToken = default)
    {
        var run = await _dbContext.AgentRuns
            .Include(x => x.Nodes)
            .Include(x => x.Events)
            .Include(x => x.ToolCalls)
            .FirstAsync(x => x.Id == runId && x.UserId == userId, cancellationToken);

        if (run.Status is AgentRunStatuses.Succeeded or AgentRunStatuses.Failed or AgentRunStatuses.Cancelled)
        {
            _logger.LogInformation(
                "Skipping agent run {AgentRunId} because status is {Status}.",
                run.Id,
                run.Status);
            return;
        }

        using var activity = EquityLensTelemetry.ActivitySource.StartActivity("agent.run.execute");
        activity?.SetTag("agent.run.id", run.Id);
        activity?.SetTag("workflow.type", run.WorkflowType);
        activity?.SetTag("agent.type", run.AgentType);

        try
        {
            var leaseOwner = $"{Environment.MachineName}:{Guid.NewGuid():N}";
            if (run.LeaseExpiresAtUtc > DateTime.UtcNow && !string.IsNullOrWhiteSpace(run.LeaseOwner)) return;
            run.LeaseOwner = leaseOwner; run.LeaseExpiresAtUtc = DateTime.UtcNow.AddMinutes(5); run.OrchestrationVersion++;
            if (run.Status == AgentRunStatuses.Pending)
            {
                _runStateMachine.Transition(run, AgentRunStatuses.Running);
                run.StartedAtUtc ??= DateTime.UtcNow;
                if ((run.WorkflowType is AgentWorkflowTypes.ResearchInvestigation or AgentWorkflowTypes.FeedbackRevision) && run.ResearchRunId is Guid researchRunId)
                {
                    var artifact = await _dbContext.ResearchRuns.SingleAsync(x => x.Id == researchRunId, cancellationToken);
                    artifact.Status = "Running";
                }
                AddEvent(run, null, AgentEventTypes.RunStarted, $"{run.WorkflowType} run started.", null);
            }
            try { await _dbContext.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException)
            {
                _dbContext.ChangeTracker.Clear();
                _logger.LogInformation("Agent run {AgentRunId} lease was claimed by another worker.", run.Id);
                return;
            }

            const int maxBatchNodes = 6;
            var batchTimer = Stopwatch.StartNew();
            AgentRunNode? lastExecutedNode = null;
            for (var batchCount = 0; batchCount < maxBatchNodes && batchTimer.Elapsed < TimeSpan.FromSeconds(30); batchCount++)
            {
                var executionOrder = _topology.GetExecutionOrder(run.WorkflowDefinitionJson);
                _runGraphValidator.Validate(run, executionOrder);
                var nodeKey = GetNextReadyNode(run, executionOrder);
                if (nodeKey is null) break;
                await RunNodeAsync(run, nodeKey, cancellationToken);
                lastExecutedNode = run.Nodes.Single(x => x.NodeKey == nodeKey);
                run.OrchestrationVersion++;
                if (run.Nodes.All(x => x.Status is AgentNodeStatuses.Succeeded or AgentNodeStatuses.Skipped))
                {
                    var graphAppended = await TryAdvanceDynamicPlanAsync(run, cancellationToken);
                    if (!graphAppended) CompleteRun(run);
                    run.LeaseOwner = null; run.LeaseExpiresAtUtc = null;
                    await SaveOrchestrationChangesAsync(graphAppended, cancellationToken);
                    return;
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
                return;
            }

            var appended = false;
            if (run.Nodes.All(x => x.Status is AgentNodeStatuses.Succeeded or AgentNodeStatuses.Skipped))
            {
                appended = await TryAdvanceDynamicPlanAsync(run, cancellationToken);
                if (!appended) CompleteRun(run);
            }
            else throw new InvalidOperationException("Workflow has no ready node but is not complete.");
            run.LeaseOwner = null; run.LeaseExpiresAtUtc = null;
            await SaveOrchestrationChangesAsync(appended, cancellationToken);
        }
        catch (Exception exception)
        {
            EquityLensTelemetry.MarkError(activity, exception);
            _logger.LogError(exception, "Agent run {AgentRunId} failed", runId);
            if (run.Status != AgentRunStatuses.Failed)
            {
                _runStateMachine.Transition(run, AgentRunStatuses.Failed);
            }
            run.ErrorMessage = exception.Message;
            run.CompletedAtUtc = DateTime.UtcNow;
            await UpdateResearchArtifactTerminalAsync(run, "Failed", cancellationToken);
            run.LeaseOwner = null; run.LeaseExpiresAtUtc = null;
            AddEvent(run, null, AgentEventTypes.RunFailed, $"{run.WorkflowType} run failed.", new { error = exception.Message });
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task RunNodeAsync(AgentRun run, string nodeKey, CancellationToken cancellationToken)
    {
        var node = run.Nodes.First(x => x.NodeKey == nodeKey);
        using var activity = EquityLensTelemetry.ActivitySource.StartActivity("agent.node.execute");
        activity?.SetTag("agent.run.id", run.Id);
        activity?.SetTag("agent.run.node.id", node.Id);
        activity?.SetTag("workflow.type", run.WorkflowType);
        activity?.SetTag("node.key", node.NodeKey);
        activity?.SetTag("node.type", node.NodeType);
        var policy = GetPolicy(run.WorkflowDefinitionJson, node.NodeKey, node.NodeType);
        var decisionPayload = new { decision = "RunNode", nextNodeId = nodeKey, reason = "Previous dependencies are satisfied.", mode = "Deterministic", policy.TimeoutSeconds, policy.MaxRetryCount };
        AddEvent(run, node, AgentEventTypes.SchedulerDecision, $"Scheduler selected {nodeKey}.", decisionPayload);
        for (var attempt = 0; attempt <= policy.MaxRetryCount; attempt++)
        {
        _nodeStateMachine.Transition(node, AgentNodeStatuses.Ready);
        AddEvent(run, node, AgentEventTypes.NodeReady, $"Node {nodeKey} is ready.", null);
        _nodeStateMachine.Transition(node, AgentNodeStatuses.Queued);
        AddEvent(run, node, AgentEventTypes.SchedulerDecision, $"Node {nodeKey} queued.", new { decision = "QueueNode", node.Iteration });
        _nodeStateMachine.Transition(node, AgentNodeStatuses.Running);
        node.StartedAtUtc = DateTime.UtcNow;
        AddEvent(run, node, AgentEventTypes.NodeStarted, $"Node {nodeKey} started.", new { attempt = attempt + 1, policy.TimeoutSeconds });
        await _dbContext.SaveChangesAsync(cancellationToken);

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
            await handler.ExecuteAsync(new AgentNodeExecutionContext(_dbContext, run, node, AddEvent), linked.Token);

            stopwatch.Stop();
            var boardAfter = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
            node.OutputBlackboardVersion = boardAfter["blackboardVersion"]?.GetValue<int>() ?? 0;
            node.ProducedBlackboardKeys = AgentNodeJson.DetectChangedKeys(boardBefore, boardAfter);
            AgentNodeJson.IncrementBlackboardVersion(boardAfter);
            run.BlackboardJson = boardAfter.ToJsonString(AgentNodeJson.SerializerOptions);
            _nodeStateMachine.Transition(node, AgentNodeStatuses.Succeeded);
            node.CompletedAtUtc = DateTime.UtcNow;
            node.DurationMs = stopwatch.ElapsedMilliseconds;
            if (run.EnableBlackboardSnapshots)
            {
                node.BlackboardSnapshotJson = run.BlackboardJson;
            }
            run.TotalInputTokens += node.InputTokens ?? 0;
            run.TotalOutputTokens += node.OutputTokens ?? 0;
            run.TotalEstimatedCostUsd += node.EstimatedCostUsd ?? 0;
            AddEvent(run, node, AgentEventTypes.NodeCompleted, $"Node {nodeKey} completed.", new { durationMs = node.DurationMs });
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }
        catch (AgentNodeException ex)
        {
            EquityLensTelemetry.MarkError(activity, ex);
            stopwatch.Stop();
            _nodeStateMachine.Transition(node, AgentNodeStatuses.Failed);
            node.ErrorMessage = ex.Message;
            node.ErrorCode = ex.ErrorCode;
            node.ErrorCategory = ex.ErrorCategory;
            node.ErrorRetryable = ex.Retryable;
            node.CompletedAtUtc = DateTime.UtcNow;
            node.DurationMs = stopwatch.ElapsedMilliseconds;
            AddEvent(run, node, AgentEventTypes.NodeFailed, $"Node {nodeKey} failed.", new { error = ex.Message, ex.ErrorCode, ex.ErrorCategory, ex.Retryable });
            if (attempt >= policy.MaxRetryCount || !ex.Retryable) throw;
            AddEvent(run, node, AgentEventTypes.SupervisorDecision, $"Retrying {nodeKey}.", new { attempt = attempt + 2, policy.MaxRetryCount });
            _nodeStateMachine.ResetForRetry(node);
            node.ErrorMessage = null; node.ErrorCode = null; node.ErrorCategory = null; node.ErrorRetryable = null;
            node.InputBlackboardVersion = null; node.OutputBlackboardVersion = null; node.ProducedBlackboardKeys = null;
            node.StartedAtUtc = null; node.CompletedAtUtc = null; node.DurationMs = null;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            EquityLensTelemetry.MarkError(activity, new TimeoutException());
            stopwatch.Stop();
            _nodeStateMachine.Transition(node, AgentNodeStatuses.Failed);
            node.ErrorMessage = $"Node timed out after {policy.TimeoutSeconds} seconds.";
            node.ErrorCode = "timeout";
            node.ErrorCategory = AgentNodeErrorCategories.TimedOut;
            node.ErrorRetryable = false;
            node.CompletedAtUtc = DateTime.UtcNow;
            node.DurationMs = stopwatch.ElapsedMilliseconds;
            AddEvent(run, node, AgentEventTypes.NodeFailed, $"Node {nodeKey} timed out.", new { policy.TimeoutSeconds });
            throw;
        }
        catch (Exception exception)
        {
            EquityLensTelemetry.MarkError(activity, exception);
            stopwatch.Stop();
            _nodeStateMachine.Transition(node, AgentNodeStatuses.Failed);
            node.ErrorMessage = exception.Message;
            node.ErrorCode = "unhandled_exception";
            node.ErrorCategory = AgentNodeErrorCategories.PermanentFailure;
            node.ErrorRetryable = false;
            node.CompletedAtUtc = DateTime.UtcNow;
            node.DurationMs = stopwatch.ElapsedMilliseconds;
            AddEvent(run, node, AgentEventTypes.NodeFailed, $"Node {nodeKey} failed.", new { error = exception.Message });
            if (attempt >= policy.MaxRetryCount) throw;
            AddEvent(run, node, AgentEventTypes.SupervisorDecision, $"Retrying {nodeKey}.", new { attempt = attempt + 2, policy.MaxRetryCount });
            _nodeStateMachine.ResetForRetry(node);
            node.ErrorMessage = null; node.ErrorCode = null; node.ErrorCategory = null; node.ErrorRetryable = null;
            node.InputBlackboardVersion = null; node.OutputBlackboardVersion = null; node.ProducedBlackboardKeys = null;
            node.StartedAtUtc = null; node.CompletedAtUtc = null; node.DurationMs = null;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
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
                var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson); var actual = board[path]?.GetValue<string>();
                if (!string.Equals(actual, expected, StringComparison.Ordinal))
                {
                    node.Status = AgentNodeStatuses.Skipped; node.CompletedAtUtc = DateTime.UtcNow;
                    AddEvent(run, node, AgentEventTypes.SchedulerDecision, $"Node {key} skipped because its condition was false.", new { decision = "SkipNode", path, expected, actual });
                    continue;
                }
            }
            return key;
        }
        return null;
    }

    private void CompleteRun(AgentRun run)
    {
        _runStateMachine.Transition(run, AgentRunStatuses.Succeeded); run.CompletedAtUtc = DateTime.UtcNow;
        if ((run.WorkflowType is AgentWorkflowTypes.ResearchInvestigation or AgentWorkflowTypes.FeedbackRevision) && run.ResearchRunId is Guid researchRunId)
        {
            var artifact = _dbContext.ResearchRuns.Local.SingleOrDefault(x => x.Id == researchRunId)
                ?? _dbContext.ResearchRuns.Single(x => x.Id == researchRunId);
            var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
            artifact.Answer = board[AgentBlackboardKeys.RevisedAnswer]?.GetValue<string>()
                ?? board[AgentBlackboardKeys.Answer]?.GetValue<string>() ?? artifact.Answer;
            artifact.Status = artifact.Answer.Contains("資料不足", StringComparison.OrdinalIgnoreCase) ? "InsufficientEvidence" : "Answered";
            artifact.LatencyMs = run.StartedAtUtc.HasValue ? (long)(DateTime.UtcNow - run.StartedAtUtc.Value).TotalMilliseconds : 0;
        }
        AddEvent(run, null, AgentEventTypes.RunSucceeded, $"{run.WorkflowType} run succeeded.", null);
    }

    private async Task<bool> TryAdvanceDynamicPlanAsync(AgentRun run, CancellationToken cancellationToken)
    {
        using var definition = JsonDocument.Parse(run.WorkflowDefinitionJson);
        if (run.WorkflowType is not (AgentWorkflowTypes.ResearchQualityReview or AgentWorkflowTypes.ResearchInvestigation or AgentWorkflowTypes.FeedbackRevision) || definition.RootElement.TryGetProperty("orchestrationMode", out var mode) is false || mode.GetString() != "DynamicStateful") return false;
        var planner = _dynamicPlanner ?? new DeterministicPlannerAdapter();
        var capabilities = _capabilities ?? new NodeCapabilityRegistry(); var skills = _skills ?? new WorkflowSkillCatalog();
        var validator = _dynamicPlanValidator ?? new DynamicPlanValidator(capabilities, _catalog ?? new AgentWorkflowCatalog(), _topology);
        var materializer = _graphMaterializer ?? new GraphMaterializer(_dbContext, _catalog ?? new AgentWorkflowCatalog());
        var last = run.Nodes.Where(x => x.Status == AgentNodeStatuses.Succeeded).OrderByDescending(x => x.CompletedAtUtc).FirstOrDefault();
        var trigger = last?.NodeType == FeedbackRevisionNodeTypes.ValidateContext && run.WorkflowType == AgentWorkflowTypes.FeedbackRevision
            ? DynamicPlanningTriggers.FeedbackContextReady
            : last?.NodeType == ResearchInvestigationNodeTypes.DetectIntent && run.WorkflowType == AgentWorkflowTypes.ResearchInvestigation
            ? DynamicPlanningTriggers.ResearchContextReady
            : last?.NodeType == ResearchInvestigationNodeTypes.EvaluateEvidence
                && AgentNodeJson.ParseBlackboard(run.BlackboardJson)[AgentBlackboardKeys.InitialEvidencePolicy]?["capabilityGate"]?.GetValue<bool>() == true
            ? DynamicPlanningTriggers.CapabilityRequestsReady
            : last?.NodeType == EvidenceRemediationNodeTypes.Route
                ? DynamicPlanningTriggers.EvidenceValidated
                : last?.NodeType == ResearchQualityReviewNodeTypes.FinalizeCriticReport
                    ? DynamicPlanningTriggers.CriticCompleted
                    : DynamicPlanningTriggers.BranchCompleted;
        if (trigger == DynamicPlanningTriggers.CriticCompleted
            && (run.WorkflowType is AgentWorkflowTypes.ResearchInvestigation or AgentWorkflowTypes.FeedbackRevision)
            && AgentNodeJson.ParseBlackboard(run.BlackboardJson)[AgentBlackboardKeys.CriticReview]?[CriticReviewFields.RecommendedNextAction]?.GetValue<string>() == "AcceptAnswer")
        {
            var completedDefinition = JsonNode.Parse(run.WorkflowDefinitionJson)!.AsObject();
            completedDefinition["goalStatus"] = DynamicGoalStatuses.Complete;
            run.WorkflowDefinitionJson = completedDefinition.ToJsonString(AgentNodeJson.SerializerOptions);
            AddEvent(run, last, AgentEventTypes.SupervisorRouteDecision, "Supervisor accepted the evidence-checked report without remediation.", new { trigger, decision = "Complete", fastPath = true });
            return false;
        }
        var dynamicNodeCount = run.Nodes.Count(x => !string.IsNullOrWhiteSpace(x.TemplateNodeKey)
            && (run.WorkflowType is not (AgentWorkflowTypes.ResearchInvestigation or AgentWorkflowTypes.FeedbackRevision) || x.Iteration > 0));
        var planningBoard = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        IReadOnlyList<WorkflowSkill> visibleSkills = skills.Skills;
        IReadOnlyList<NodeCapability> visibleCapabilities = capabilities.Capabilities;
        if ((trigger is DynamicPlanningTriggers.ResearchContextReady or DynamicPlanningTriggers.CapabilityRequestsReady)
            && planningBoard[AgentBlackboardKeys.LeadSkill]?.GetValue<string>() is { Length: > 0 } leadSkillId)
        {
            var leadSkill = skills.Skills.SingleOrDefault(x => x.Id == leadSkillId && x.Routable)
                ?? throw new InvalidOperationException($"Routed lead skill '{leadSkillId}' is not registered.");
            visibleSkills = [leadSkill];
            var allowed = leadSkill.Capabilities.ToHashSet(StringComparer.Ordinal);
            visibleCapabilities = capabilities.Capabilities.Where(x => allowed.Contains(x.Id)).ToList();
        }
        var context = new WorkflowPlanningContext(run.Id, run.OrchestrationVersion, trigger, planningBoard, run.Nodes.Where(x => x.Status == AgentNodeStatuses.Succeeded).Select(x => x.NodeType).ToList(), visibleSkills, visibleCapabilities, run.Nodes.Where(x => (x.NodeType is EvidenceRemediationNodeTypes.RetrieveEvidence or EvidenceRemediationNodeTypes.RetrieveWebEvidence) && x.Status == AgentNodeStatuses.Succeeded).Select(x => x.Iteration).DefaultIfEmpty(0).Max(), dynamicNodeCount, last?.NodeKey);
        var started = DateTime.UtcNow;
        AddEvent(run, last, AgentEventTypes.SupervisorPlanningStarted, $"Supervisor planning started for {trigger}.", new { trigger, context.OrchestrationVersion });
        var planningTimeoutSeconds = LlmAgentWorkflowPlanner.TimeoutSeconds;
        var plannerCall = new AgentToolCall { Id = Guid.NewGuid(), AgentRunId = run.Id, AgentRunNodeId = last?.Id, ToolName = "workflowPlannerLLM", Status = AgentToolCallStatuses.Running, ArgumentsJson = Serialize(new { trigger, promptTemplateId = LlmAgentWorkflowPlanner.PromptTemplateId, promptVersion = LlmAgentWorkflowPlanner.PromptVersion, timeoutSeconds = planningTimeoutSeconds, context.OrchestrationVersion }), StartedAtUtc = started };
        _dbContext.AgentToolCalls.Add(plannerCall); AddEvent(run, last, AgentEventTypes.ToolCallStarted, "Tool workflowPlannerLLM started.", new { trigger, timeoutSeconds = planningTimeoutSeconds });
        DynamicPlanProposal proposal;
        try
        {
            DynamicPlanProposal? planned = null; Exception? planningError = null;
            var completed = new ManualResetEventSlim(false);
            var plannerThread = new Thread(() =>
            {
                try { planned = planner.PlanAsync(context, cancellationToken).GetAwaiter().GetResult(); }
                catch (Exception exception) { planningError = exception; }
                finally { completed.Set(); }
            }) { IsBackground = true, Name = $"workflow-planner-{run.Id:N}" };
            plannerThread.Start();
            var deadline = Stopwatch.StartNew();
            while (!completed.IsSet && deadline.Elapsed < TimeSpan.FromSeconds(planningTimeoutSeconds))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Thread.Sleep(100);
            }
            if (!completed.IsSet) proposal = DeterministicDynamicWorkflowPlanner.Create(context, $"Workflow planner exceeded the orchestrator {planningTimeoutSeconds} second deadline.");
            else if (planningError is not null) throw planningError;
            else proposal = planned ?? throw new InvalidOperationException("Workflow planner returned no proposal.");
            plannerCall.Status = AgentToolCallStatuses.Succeeded; plannerCall.ResultPreview = AgentNodeJson.Trim(proposal.Reason, 180); plannerCall.ResultJson = Serialize(proposal); plannerCall.CompletedAtUtc = DateTime.UtcNow; plannerCall.DurationMs = (long)(DateTime.UtcNow - started).TotalMilliseconds; AddEvent(run, last, AgentEventTypes.ToolCallCompleted, "Tool workflowPlannerLLM completed.", new { plannerCall.DurationMs, proposal.Mode });
        }
        catch (Exception exception)
        {
            plannerCall.Status = AgentToolCallStatuses.Failed; plannerCall.ErrorMessage = exception.Message; plannerCall.CompletedAtUtc = DateTime.UtcNow; plannerCall.DurationMs = (long)(DateTime.UtcNow - started).TotalMilliseconds; AddEvent(run, last, AgentEventTypes.ToolCallFailed, "Tool workflowPlannerLLM failed.", new { error = exception.Message }); throw;
        }
        AddEvent(run, last, AgentEventTypes.PlannerProposed, proposal.Reason, new { proposal.ProposalId, proposal.Trigger, proposal.GoalStatus, proposal.SelectedSkills, proposal.Mode, proposal.Provider, proposal.Model, proposal.PromptTokens, proposal.CompletionTokens, proposal.FallbackReason, actions = proposal.Actions.Select(x => new { x.ClientNodeKey, x.Capability, x.NodeType }) });
        ValidatedDynamicPlan? validated = null;
        try { validated = validator.Validate(run, proposal); AddEvent(run, last, AgentEventTypes.PlanValidated, "Dynamic plan validated.", new { proposal.ProposalId, actionCount = proposal.Actions.Count }); }
        catch (Exception exception)
        {
            AddEvent(run, last, AgentEventTypes.PlanRejected, exception.Message, new { proposal.ProposalId, error = exception.Message });
            if (validated is null)
            {
                proposal = DeterministicDynamicWorkflowPlanner.Create(context, exception.Message, proposal.Model);
                AddEvent(run, last, AgentEventTypes.PlannerProposed, proposal.Reason, new { proposal.ProposalId, proposal.Trigger, proposal.GoalStatus, proposal.SelectedSkills, proposal.Mode, proposal.FallbackReason, actions = proposal.Actions.Select(x => new { x.ClientNodeKey, x.Capability, x.NodeType }) });
                validated = validator.Validate(run, proposal);
                AddEvent(run, last, AgentEventTypes.PlanValidated, "Deterministic fallback plan validated.", new { proposal.ProposalId, actionCount = proposal.Actions.Count });
            }
        }
        if (proposal.GoalStatus == DynamicGoalStatuses.Complete)
        {
            var json = JsonNode.Parse(run.WorkflowDefinitionJson)!.AsObject(); json["goalStatus"] = DynamicGoalStatuses.Complete; run.WorkflowDefinitionJson = json.ToJsonString(AgentNodeJson.SerializerOptions); return false;
        }
        materializer.Materialize(run, validated!);
        return true;
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

    private sealed class DeterministicPlannerAdapter : IAgentWorkflowPlanner
    {
        public Task<DynamicPlanProposal> PlanAsync(WorkflowPlanningContext context, CancellationToken cancellationToken = default) => Task.FromResult(DeterministicDynamicWorkflowPlanner.Create(context, "Dynamic LLM planner is not registered."));
    }

    private void AddWakeOutbox(AgentRun run, AgentRunNode cause)
    {
        var version = JsonNode.Parse(run.WorkflowDefinitionJson)?["version"]?.GetValue<int>() ?? 1;
        _dbContext.AgentRunWakeOutbox.Add(new AgentRunWakeOutbox { Id = Guid.NewGuid(), AgentRunId = run.Id, UserId = run.UserId, WorkflowType = run.WorkflowType, AgentRunNodeId = cause.Id, DefinitionVersion = version, OrchestrationVersion = run.OrchestrationVersion, CorrelationId = run.Id, CausationId = cause.Id });
    }

    private async Task UpdateResearchArtifactTerminalAsync(AgentRun run, string status, CancellationToken cancellationToken)
    {
        if (run.WorkflowType is not (AgentWorkflowTypes.ResearchInvestigation or AgentWorkflowTypes.FeedbackRevision) || run.ResearchRunId is not Guid researchRunId) return;
        var artifact = await _dbContext.ResearchRuns.SingleOrDefaultAsync(x => x.Id == researchRunId, cancellationToken);
        if (artifact is null) return;
        artifact.Status = status; artifact.ErrorMessage = run.ErrorMessage;
        artifact.LatencyMs = run.StartedAtUtc.HasValue ? (long)(DateTime.UtcNow - run.StartedAtUtc.Value).TotalMilliseconds : 0;
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

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, SerializerOptions);

    private AgentNodeExecutionPolicy GetPolicy(string definition, string nodeKey, string nodeType)
    {
        var node = JsonNode.Parse(definition)?["nodes"]?.AsArray().OfType<JsonObject>().SingleOrDefault(x => x["id"]?.GetValue<string>() == nodeKey);
        var policy = node?["executionPolicy"] as JsonObject;
        return policy is null ? _catalog?.GetNode(nodeType).DefaultPolicy ?? new AgentNodeExecutionPolicy(120, 0) : new(policy["timeoutSeconds"]!.GetValue<int>(), policy["maxRetryCount"]!.GetValue<int>());
    }
}
