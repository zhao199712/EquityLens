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
                AddEvent(run, null, AgentEventTypes.RunStarted, $"{run.WorkflowType} run started.", null);
            }
            try { await _dbContext.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException)
            {
                _dbContext.ChangeTracker.Clear();
                _logger.LogInformation("Agent run {AgentRunId} lease was claimed by another worker.", run.Id);
                return;
            }

            var executionOrder = _topology.GetExecutionOrder(run.WorkflowDefinitionJson);
            _runGraphValidator.Validate(run, executionOrder);
            var nodeKey = GetNextReadyNode(run, executionOrder);
            if (nodeKey is not null)
            {
                await RunNodeAsync(run, nodeKey, cancellationToken);
                run.LeaseOwner = null; run.LeaseExpiresAtUtc = null; run.OrchestrationVersion++;
                if (run.Nodes.All(x => x.Status is AgentNodeStatuses.Succeeded or AgentNodeStatuses.Skipped))
                {
                    if (!await TryAdvanceDynamicPlanAsync(run, cancellationToken)) CompleteRun(run);
                }
                else AddWakeOutbox(run, run.Nodes.Single(x => x.NodeKey == nodeKey));
                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            if (run.Nodes.All(x => x.Status is AgentNodeStatuses.Succeeded or AgentNodeStatuses.Skipped))
            {
                if (!await TryAdvanceDynamicPlanAsync(run, cancellationToken)) CompleteRun(run);
            }
            else throw new InvalidOperationException("Workflow has no ready node but is not complete.");
            run.LeaseOwner = null; run.LeaseExpiresAtUtc = null;
            await _dbContext.SaveChangesAsync(cancellationToken);
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
        AddEvent(run, node, AgentEventTypes.SupervisorDecision, $"Supervisor selected {nodeKey}.", decisionPayload);
        for (var attempt = 0; attempt <= policy.MaxRetryCount; attempt++)
        {
        _nodeStateMachine.Transition(node, AgentNodeStatuses.Ready);
        AddEvent(run, node, AgentEventTypes.NodeReady, $"Node {nodeKey} is ready.", null);
        _nodeStateMachine.Transition(node, AgentNodeStatuses.Queued);
        AddEvent(run, node, AgentEventTypes.SupervisorDecision, $"Node {nodeKey} queued.", new { decision = "QueueNode", node.Iteration });
        _nodeStateMachine.Transition(node, AgentNodeStatuses.Running);
        node.StartedAtUtc = DateTime.UtcNow;
        AddEvent(run, node, AgentEventTypes.NodeStarted, $"Node {nodeKey} started.", new { attempt = attempt + 1, policy.TimeoutSeconds });
        await _dbContext.SaveChangesAsync(cancellationToken);

        var stopwatch = Stopwatch.StartNew();
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
            _nodeStateMachine.Transition(node, AgentNodeStatuses.Succeeded);
            node.CompletedAtUtc = DateTime.UtcNow;
            node.DurationMs = stopwatch.ElapsedMilliseconds;
            AddEvent(run, node, AgentEventTypes.NodeCompleted, $"Node {nodeKey} completed.", new { durationMs = node.DurationMs });
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }
        catch (Exception exception)
        {
            EquityLensTelemetry.MarkError(activity, exception);
            stopwatch.Stop();
            _nodeStateMachine.Transition(node, AgentNodeStatuses.Failed);
            node.ErrorMessage = exception is OperationCanceledException && !cancellationToken.IsCancellationRequested ? $"Node timed out after {policy.TimeoutSeconds} seconds." : exception.Message;
            node.CompletedAtUtc = DateTime.UtcNow;
            node.DurationMs = stopwatch.ElapsedMilliseconds;
            AddEvent(run, node, AgentEventTypes.NodeFailed, $"Node {nodeKey} failed.", new { error = exception.Message });
            if (attempt >= policy.MaxRetryCount) throw;
            AddEvent(run, node, AgentEventTypes.SupervisorDecision, $"Retrying {nodeKey}.", new { attempt = attempt + 2, policy.MaxRetryCount });
            _nodeStateMachine.ResetForRetry(node);
            node.ErrorMessage = null; node.StartedAtUtc = null; node.CompletedAtUtc = null; node.DurationMs = null;
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
                    AddEvent(run, node, AgentEventTypes.SupervisorDecision, $"Node {key} skipped because its condition was false.", new { decision = "SkipNode", path, expected, actual });
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
        AddEvent(run, null, AgentEventTypes.RunSucceeded, $"{run.WorkflowType} run succeeded.", null);
    }

    private async Task<bool> TryAdvanceDynamicPlanAsync(AgentRun run, CancellationToken cancellationToken)
    {
        using var definition = JsonDocument.Parse(run.WorkflowDefinitionJson);
        if (run.WorkflowType != AgentWorkflowTypes.ResearchQualityReview || definition.RootElement.TryGetProperty("orchestrationMode", out var mode) is false || mode.GetString() != "DynamicStateful") return false;
        var planner = _dynamicPlanner ?? new DeterministicPlannerAdapter();
        var capabilities = _capabilities ?? new NodeCapabilityRegistry(); var skills = _skills ?? new WorkflowSkillCatalog();
        var validator = _dynamicPlanValidator ?? new DynamicPlanValidator(capabilities, _catalog ?? new AgentWorkflowCatalog(), _topology);
        var materializer = _graphMaterializer ?? new GraphMaterializer(_dbContext, _catalog ?? new AgentWorkflowCatalog());
        var last = run.Nodes.Where(x => x.Status == AgentNodeStatuses.Succeeded).OrderByDescending(x => x.CompletedAtUtc).FirstOrDefault();
        var trigger = last?.NodeType == EvidenceRemediationNodeTypes.Route ? DynamicPlanningTriggers.EvidenceValidated : last?.NodeType == ResearchQualityReviewNodeTypes.FinalizeCriticReport ? DynamicPlanningTriggers.CriticCompleted : DynamicPlanningTriggers.BranchCompleted;
        var context = new WorkflowPlanningContext(run.Id, run.OrchestrationVersion, trigger, AgentNodeJson.ParseBlackboard(run.BlackboardJson), run.Nodes.Where(x => x.Status == AgentNodeStatuses.Succeeded).Select(x => x.NodeType).ToList(), skills.Skills, capabilities.Capabilities, run.Nodes.Where(x => x.NodeType == EvidenceRemediationNodeTypes.RetrieveEvidence && x.Status == AgentNodeStatuses.Succeeded).Select(x => x.Iteration).DefaultIfEmpty(0).Max(), run.Nodes.Count - 5);
        var started = DateTime.UtcNow;
        var plannerCall = new AgentToolCall { Id = Guid.NewGuid(), AgentRunId = run.Id, AgentRunNodeId = last?.Id, ToolName = "workflowPlannerLLM", Status = AgentToolCallStatuses.Running, ArgumentsJson = Serialize(new { trigger, promptTemplateId = LlmAgentWorkflowPlanner.PromptTemplateId, promptVersion = LlmAgentWorkflowPlanner.PromptVersion, timeoutSeconds = 45, context.OrchestrationVersion }), StartedAtUtc = started };
        _dbContext.AgentToolCalls.Add(plannerCall); AddEvent(run, last, AgentEventTypes.ToolCallStarted, "Tool workflowPlannerLLM started.", new { trigger, timeoutSeconds = 45 });
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
            while (!completed.IsSet && deadline.Elapsed < TimeSpan.FromSeconds(45))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Thread.Sleep(100);
            }
            if (!completed.IsSet) proposal = DeterministicDynamicWorkflowPlanner.Create(context, "Workflow planner exceeded the orchestrator 45 second deadline.");
            else if (planningError is not null) throw planningError;
            else proposal = planned ?? throw new InvalidOperationException("Workflow planner returned no proposal.");
            plannerCall.Status = AgentToolCallStatuses.Succeeded; plannerCall.ResultPreview = AgentNodeJson.Trim(proposal.Reason, 180); plannerCall.ResultJson = Serialize(proposal); plannerCall.CompletedAtUtc = DateTime.UtcNow; plannerCall.DurationMs = (long)(DateTime.UtcNow - started).TotalMilliseconds; AddEvent(run, last, AgentEventTypes.ToolCallCompleted, "Tool workflowPlannerLLM completed.", new { plannerCall.DurationMs, proposal.Mode });
        }
        catch (Exception exception)
        {
            plannerCall.Status = AgentToolCallStatuses.Failed; plannerCall.ErrorMessage = exception.Message; plannerCall.CompletedAtUtc = DateTime.UtcNow; plannerCall.DurationMs = (long)(DateTime.UtcNow - started).TotalMilliseconds; AddEvent(run, last, AgentEventTypes.ToolCallFailed, "Tool workflowPlannerLLM failed.", new { error = exception.Message }); throw;
        }
        AddEvent(run, last, AgentEventTypes.PlannerProposed, proposal.Reason, new { proposal.ProposalId, proposal.Trigger, proposal.GoalStatus, proposal.SelectedSkills, proposal.Mode, proposal.Provider, proposal.Model, proposal.PromptTokens, proposal.CompletionTokens, proposal.FallbackReason, actions = proposal.Actions.Select(x => new { x.ClientNodeKey, x.Capability, x.NodeType }) });
        ValidatedDynamicPlan validated;
        try { validated = validator.Validate(run, proposal); AddEvent(run, last, AgentEventTypes.PlanValidated, "Dynamic plan validated.", new { proposal.ProposalId, actionCount = proposal.Actions.Count }); }
        catch (Exception exception)
        {
            AddEvent(run, last, AgentEventTypes.PlanRejected, exception.Message, new { proposal.ProposalId, error = exception.Message });
            proposal = DeterministicDynamicWorkflowPlanner.Create(context, exception.Message, proposal.Model);
            AddEvent(run, last, AgentEventTypes.PlannerProposed, proposal.Reason, new { proposal.ProposalId, proposal.Trigger, proposal.GoalStatus, proposal.SelectedSkills, proposal.Mode, proposal.FallbackReason, actions = proposal.Actions.Select(x => new { x.ClientNodeKey, x.Capability, x.NodeType }) });
            validated = validator.Validate(run, proposal);
            AddEvent(run, last, AgentEventTypes.PlanValidated, "Deterministic fallback plan validated.", new { proposal.ProposalId, actionCount = proposal.Actions.Count });
        }
        if (proposal.GoalStatus == DynamicGoalStatuses.Complete)
        {
            var json = JsonNode.Parse(run.WorkflowDefinitionJson)!.AsObject(); json["goalStatus"] = DynamicGoalStatuses.Complete; run.WorkflowDefinitionJson = json.ToJsonString(AgentNodeJson.SerializerOptions); return false;
        }
        var existingNodeIds = run.Nodes.Select(x => x.Id).ToHashSet();
        materializer.Materialize(run, validated);
        // Appending a graph patch must not rewrite completed executions. Some providers
        // mark the existing relationship members as modified when new nodes are added;
        // keeping them unchanged also prevents an unrelated stale node row from rolling
        // back the otherwise atomic planner/tool-call/graph-patch transaction.
        foreach (var entry in _dbContext.ChangeTracker.Entries<AgentRunNode>()
                     .Where(x => existingNodeIds.Contains(x.Entity.Id) && x.State == EntityState.Modified))
        {
            entry.OriginalValues.SetValues(entry.CurrentValues);
            entry.State = EntityState.Unchanged;
        }
        return true;
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
