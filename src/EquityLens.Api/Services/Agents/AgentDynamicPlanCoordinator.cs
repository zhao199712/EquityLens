using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Services.Agents;

public interface IAgentDynamicPlanCoordinator
{
    Task<bool> TryAdvanceAsync(AgentRun run, AgentRunEventWriter addEvent, CancellationToken cancellationToken);
}

/// <summary>協調動態規劃、迴圈決策與圖擴充，由 executor 提交儲存。</summary>
public sealed class AgentDynamicPlanCoordinator(
    EquityLensDbContext dbContext,
    IAgentWorkflowPlanner planner,
    IDynamicPlanValidator validator,
    IGraphMaterializer materializer,
    IWorkflowSkillCatalog skills,
    INodeCapabilityRegistry capabilities,
    IAgentLoopController loopController,
    AgentWorkflowPolicyReader policies) : IAgentDynamicPlanCoordinator
{
    public async Task<bool> TryAdvanceAsync(AgentRun run, AgentRunEventWriter addEvent, CancellationToken cancellationToken)
    {
        using var definition = JsonDocument.Parse(run.WorkflowDefinitionJson);
        if (run.WorkflowType is not (AgentWorkflowTypes.ResearchQualityReview or AgentWorkflowTypes.ResearchInvestigation or AgentWorkflowTypes.FeedbackRevision or AgentWorkflowTypes.PortfolioDiagnosis) || definition.RootElement.TryGetProperty("orchestrationMode", out var mode) is false || mode.GetString() != "DynamicStateful") return false;
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
        AgentLoopDecision? loopDecision = null;
        if (run.WorkflowType is AgentWorkflowTypes.ResearchQualityReview or AgentWorkflowTypes.PortfolioDiagnosis)
        {
            var controller = loopController;
            loopDecision = controller.Evaluate(run)
                ?? throw new InvalidOperationException("ResearchQualityReview loop policy is not registered.");
            var decision = loopDecision;
            if (!run.Events.Any(x => x.EventType == AgentEventTypes.LoopStarted))
                addEvent(run, last, AgentEventTypes.LoopStarted, $"{run.WorkflowType} loop started.", run.WorkflowType == AgentWorkflowTypes.PortfolioDiagnosis
                    ? new { profileVersion = PortfolioDiagnosisWorkflow.LoopProfileVersion, maxIterations = PortfolioDiagnosisWorkflow.MaxAnalysisIterations, maxDynamicNodes = PortfolioDiagnosisWorkflow.MaxDynamicNodes, maxWebRetrievals = 0 }
                    : new { profileVersion = ResearchQualityReviewWorkflow.LoopProfileVersion, maxIterations = ResearchQualityReviewWorkflow.MaxRetrievalIterations, maxDynamicNodes = ResearchQualityReviewWorkflow.MaxDynamicNodes, maxWebRetrievals = ResearchQualityReviewWorkflow.MaxWebRetrievals });
            if (run.WorkflowType == AgentWorkflowTypes.ResearchQualityReview && last?.NodeType == EvidenceRemediationNodeTypes.Route)
                addEvent(run, last, AgentEventTypes.LoopIterationCompleted, $"Loop iteration {last.Iteration} completed.", new { last.Iteration, decision.EvidenceCount, decision.UnresolvedClaimIds });
            addEvent(run, last, AgentEventTypes.LoopDecisionMade, decision.Reason, decision);
            if (decision.IsTerminal)
            {
                var completedDefinition = JsonNode.Parse(run.WorkflowDefinitionJson)!.AsObject();
                completedDefinition["goalStatus"] = DynamicGoalStatuses.Complete;
                run.WorkflowDefinitionJson = completedDefinition.ToJsonString(AgentNodeJson.SerializerOptions);
                addEvent(run, last, AgentEventTypes.LoopStopped, $"Research quality loop stopped: {decision.ReasonCode}.", decision);
                return false;
            }
            addEvent(run, last, AgentEventTypes.LoopIterationStarted, $"Loop iteration {decision.Iteration} started.", decision);
        }
        if (trigger == DynamicPlanningTriggers.CriticCompleted
            && (run.WorkflowType is AgentWorkflowTypes.ResearchInvestigation or AgentWorkflowTypes.FeedbackRevision)
            && AgentNodeJson.ParseBlackboard(run.BlackboardJson)[AgentBlackboardKeys.CriticReview]?[CriticReviewFields.RecommendedNextAction]?.GetValue<string>() == "AcceptAnswer")
        {
            var completedDefinition = JsonNode.Parse(run.WorkflowDefinitionJson)!.AsObject();
            completedDefinition["goalStatus"] = DynamicGoalStatuses.Complete;
            run.WorkflowDefinitionJson = completedDefinition.ToJsonString(AgentNodeJson.SerializerOptions);
            addEvent(run, last, AgentEventTypes.SupervisorRouteDecision, "Supervisor accepted the evidence-checked report without remediation.", new { trigger, decision = "Complete", fastPath = true });
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
        var context = new WorkflowPlanningContext(run.Id, run.OrchestrationVersion, trigger, planningBoard, run.Nodes.Where(x => x.Status == AgentNodeStatuses.Succeeded).Select(x => x.NodeType).ToList(), visibleSkills, visibleCapabilities, run.Nodes.Where(x => (x.NodeType is EvidenceRemediationNodeTypes.RetrieveEvidence or EvidenceRemediationNodeTypes.RetrieveWebEvidence) && x.Status == AgentNodeStatuses.Succeeded).Select(x => x.Iteration).DefaultIfEmpty(0).Max(), dynamicNodeCount, last?.NodeKey, loopDecision);
        var started = DateTime.UtcNow;
        addEvent(run, last, AgentEventTypes.SupervisorPlanningStarted, $"Supervisor planning started for {trigger}.", new { trigger, context.OrchestrationVersion });
        var planningTimeoutSeconds = LlmAgentWorkflowPlanner.TimeoutSeconds;
        var plannerToolName = loopDecision is null ? "workflowPlannerLLM" : "loopPlanBuilder";
        var plannerCall = new AgentToolCall { Id = Guid.NewGuid(), AgentRunId = run.Id, AgentRunNodeId = last?.Id, ToolName = plannerToolName, Status = AgentToolCallStatuses.Running, ArgumentsJson = Serialize(new { trigger, loopDecision, promptTemplateId = loopDecision is null ? LlmAgentWorkflowPlanner.PromptTemplateId : null, promptVersion = loopDecision is null ? LlmAgentWorkflowPlanner.PromptVersion : (int?)null, timeoutSeconds = planningTimeoutSeconds, context.OrchestrationVersion }), StartedAtUtc = started };
        dbContext.AgentToolCalls.Add(plannerCall); addEvent(run, last, AgentEventTypes.ToolCallStarted, $"Tool {plannerToolName} started.", new { trigger, timeoutSeconds = planningTimeoutSeconds });
        DynamicPlanProposal proposal;
        try
        {
            DynamicPlanProposal? planned = null; Exception? planningError = null;
            if (loopDecision is not null)
            {
                planned = DeterministicDynamicWorkflowPlanner.Create(context);
            }
            var completed = new ManualResetEventSlim(false);
            var plannerThread = new Thread(() =>
            {
                try { planned ??= planner.PlanAsync(context, cancellationToken).GetAwaiter().GetResult(); }
                catch (Exception exception) { planningError = exception; }
                finally { completed.Set(); }
            })
            { IsBackground = true, Name = $"workflow-planner-{run.Id:N}" };
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
            plannerCall.Status = AgentToolCallStatuses.Succeeded; plannerCall.ResultPreview = AgentNodeJson.Trim(proposal.Reason, 180); plannerCall.ResultJson = Serialize(proposal); plannerCall.CompletedAtUtc = DateTime.UtcNow; plannerCall.DurationMs = (long)(DateTime.UtcNow - started).TotalMilliseconds; addEvent(run, last, AgentEventTypes.ToolCallCompleted, $"Tool {plannerToolName} completed.", new { plannerCall.DurationMs, proposal.Mode });
        }
        catch (Exception exception)
        {
            plannerCall.Status = AgentToolCallStatuses.Failed; plannerCall.ErrorMessage = exception.Message; plannerCall.CompletedAtUtc = DateTime.UtcNow; plannerCall.DurationMs = (long)(DateTime.UtcNow - started).TotalMilliseconds; addEvent(run, last, AgentEventTypes.ToolCallFailed, $"Tool {plannerToolName} failed.", new { error = exception.Message }); throw;
        }
        addEvent(run, last, AgentEventTypes.PlannerProposed, proposal.Reason, new { proposal.ProposalId, proposal.Trigger, proposal.GoalStatus, proposal.SelectedSkills, proposal.Mode, proposal.Provider, proposal.Model, proposal.PromptTokens, proposal.CompletionTokens, proposal.FallbackReason, actions = proposal.Actions.Select(x => new { x.ClientNodeKey, x.Capability, x.NodeType }) });
        ValidatedDynamicPlan? validated = null;
        try { validated = validator.Validate(run, proposal); addEvent(run, last, AgentEventTypes.PlanValidated, "Dynamic plan validated.", new { proposal.ProposalId, actionCount = proposal.Actions.Count }); }
        catch (Exception exception)
        {
            addEvent(run, last, AgentEventTypes.PlanRejected, exception.Message, new { proposal.ProposalId, error = exception.Message });
            if (validated is null)
            {
                proposal = DeterministicDynamicWorkflowPlanner.Create(context, exception.Message, proposal.Model);
                addEvent(run, last, AgentEventTypes.PlannerProposed, proposal.Reason, new { proposal.ProposalId, proposal.Trigger, proposal.GoalStatus, proposal.SelectedSkills, proposal.Mode, proposal.FallbackReason, actions = proposal.Actions.Select(x => new { x.ClientNodeKey, x.Capability, x.NodeType }) });
                try
                {
                    validated = validator.Validate(run, proposal);
                }
                catch (Exception repairException)
                {
                    addEvent(run, last, AgentEventTypes.PlanRejected, repairException.Message, new { proposal.ProposalId, error = repairException.Message, repairAttempt = true });
                    if (loopDecision is null) throw;
                    throw new AgentLoopPlanValidationException($"Dynamic plan repair failed validation: {repairException.Message}", repairException);
                }
                addEvent(run, last, AgentEventTypes.PlanValidated, "Deterministic fallback plan validated.", new { proposal.ProposalId, actionCount = proposal.Actions.Count });
            }
        }
        if (proposal.GoalStatus == DynamicGoalStatuses.Complete)
        {
            var json = JsonNode.Parse(run.WorkflowDefinitionJson)!.AsObject(); json["goalStatus"] = DynamicGoalStatuses.Complete; run.WorkflowDefinitionJson = json.ToJsonString(AgentNodeJson.SerializerOptions); return false;
        }
        materializer.Materialize(run, validated!);
        await policies.SnapshotMissingApprovalPoliciesAsync(run, cancellationToken);
        return true;
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, AgentNodeJson.SerializerOptions);
}
