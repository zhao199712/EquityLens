using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Ai;

namespace EquityLens.Api.Services.Agents;

public static class DynamicGoalStatuses { public const string Continue = "Continue"; public const string Complete = "Complete"; }
public static class DynamicPlanningTriggers { public const string CriticCompleted = "CriticCompleted"; public const string BranchCompleted = "BranchCompleted"; public const string EvidenceValidated = "EvidenceValidated"; }

public sealed record WorkflowSkill(string Id, string Description, IReadOnlyList<string> Capabilities);
public interface IWorkflowSkillCatalog { IReadOnlyList<WorkflowSkill> Skills { get; } }
public sealed class WorkflowSkillCatalog : IWorkflowSkillCatalog
{
    public IReadOnlyList<WorkflowSkill> Skills { get; } =
    [
        new("evidence-remediation", "Close citation and evidence gaps with bounded local or Web retrieval, validation, and revision.", ["extract-claims", "retrieve-primary-financial-evidence", "retrieve-web-evidence", "assess-claim-evidence", "validate-evidence", "build-evidence-packet", "revise-with-evidence", "finalize-quality"]),
        new("financial-guidance-verification", "Verify financial guidance with primary or supporting evidence.", ["retrieve-primary-financial-evidence", "assess-claim-evidence", "validate-evidence"]),
        new("evidence-driven-reanalysis", "Reanalyze material conclusions using validated evidence only.", ["build-analysis-context", "reanalyze-investment-answer", "critique-reanalysis", "revise-reanalysis", "finalize-reanalysis"]),
        new("answer-revision", "Revise wording or conclusions from critic findings.", ["revise-answer", "finalize-revision"]),
        new("quality-finalization", "Finish the quality workflow without adding unsupported work.", ["finalize-quality"])
    ];
}

public sealed record NodeCapability(string Id, string NodeType, string Description, string ArgumentSchema, IReadOnlyList<string> RequiredKeys, IReadOnlyList<string> ProducedKeys, string SideEffectLevel, bool Idempotent, bool SupportsLoop, int MaxOccurrences);
public interface INodeCapabilityRegistry { IReadOnlyList<NodeCapability> Capabilities { get; } NodeCapability Get(string id); }
public sealed class NodeCapabilityRegistry : INodeCapabilityRegistry
{
    public IReadOnlyList<NodeCapability> Capabilities { get; } =
    [
        C("extract-claims", EvidenceRemediationNodeTypes.ExtractClaims, "Extract evidence-bearing claims from the answer."),
        C("retrieve-primary-financial-evidence", EvidenceRemediationNodeTypes.RetrieveEvidence, "Retrieve local primary evidence without hidden Web access in dynamic workflows.", loop: true, max: 2),
        C("retrieve-web-evidence", EvidenceRemediationNodeTypes.RetrieveWebEvidence, "Retrieve current Web evidence through the configured provider when freshness or local evidence gaps require it."),
        C("assess-claim-evidence", EvidenceRemediationNodeTypes.AssessSupport, "Assess semantic support and material analysis impact.", max: 2),
        C("validate-evidence", EvidenceRemediationNodeTypes.ValidateMappings, "Deterministically validate claim/evidence mappings.", max: 2),
        C("route-evidence", EvidenceRemediationNodeTypes.Route, "Persist the evidence route decision.", max: 2),
        C("build-evidence-packet", EvidenceRemediationNodeTypes.BuildPacket, "Build a packet containing validated evidence only."),
        C("revise-with-evidence", EvidenceRemediationNodeTypes.DraftRevision, "Revise using the validated evidence packet."),
        C("finalize-quality", EvidenceRemediationNodeTypes.Finalize, "Finalize a quality result."),
        C("revise-answer", DraftRevisionNodeTypes.DraftRevisedAnswer, "Apply critic findings to the answer."),
        C("finalize-revision", DraftRevisionNodeTypes.FinalizeRevision, "Finalize a critic-driven revision."),
        C("build-analysis-context", EvidenceReanalysisNodeTypes.BuildContext, "Build a validated evidence-only reanalysis context."),
        C("reanalyze-investment-answer", EvidenceReanalysisNodeTypes.Reanalyze, "Reanalyze material conclusions without searching."),
        C("critique-reanalysis", EvidenceReanalysisNodeTypes.Critique, "Independently critique the reanalysis."),
        C("revise-reanalysis", EvidenceReanalysisNodeTypes.Revise, "Apply one bounded reanalysis revision."),
        C("finalize-reanalysis", EvidenceReanalysisNodeTypes.Finalize, "Finalize the reanalysis result.")
    ];
    public NodeCapability Get(string id) => Capabilities.Single(x => x.Id == id);
    private static NodeCapability C(string id, string type, string description, bool loop = false, int max = 1)
    {
        var contract = new AgentWorkflowCatalog().GetNode(type).Contract;
        return new(id, type, description, type is EvidenceRemediationNodeTypes.RetrieveEvidence or EvidenceRemediationNodeTypes.RetrieveWebEvidence ? "RetrieveEvidencePlannerArguments" : contract.InputSchema, contract.RequiredBlackboardKeys, contract.ProducedBlackboardKeys, contract.SideEffectLevel, contract.IsIdempotent, loop || contract.SupportsLoop, max);
    }
}

public sealed record DynamicPlanCondition(string Path, string ExpectedValue);
public sealed record DynamicPlanAction(string ClientNodeKey, string Capability, string NodeType, IReadOnlyList<string> DependsOn, JsonObject Arguments, DynamicPlanCondition? Condition = null, int Iteration = 0);
public sealed record DynamicPlanProposal(Guid ProposalId, long BaseOrchestrationVersion, string Trigger, string GoalStatus, string Reason, IReadOnlyList<string> SelectedSkills, IReadOnlyList<DynamicPlanAction> Actions, string Mode = "Llm", string? Model = null, int PromptTokens = 0, int CompletionTokens = 0, string? FallbackReason = null, string? Provider = null);
public sealed record WorkflowPlanningContext(Guid RunId, long OrchestrationVersion, string Trigger, JsonObject Blackboard, IReadOnlyList<string> CompletedNodeTypes, IReadOnlyList<WorkflowSkill> Skills, IReadOnlyList<NodeCapability> Capabilities, int RetrievalIterations, int DynamicNodeCount);
public interface IAgentWorkflowPlanner { Task<DynamicPlanProposal> PlanAsync(WorkflowPlanningContext context, CancellationToken cancellationToken = default); }

public sealed class LlmAgentWorkflowPlanner(IChatCompletionService chat) : IAgentWorkflowPlanner
{
    public const string PromptTemplateId = "research-quality-workflow-planner";
    public const int PromptVersion = 1;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<DynamicPlanProposal> PlanAsync(WorkflowPlanningContext context, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        string? error = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var completion = chat.CompleteAsync(new ChatCompletionRequest(SystemPrompt, JsonSerializer.Serialize(new
                {
                    context.RunId, context.OrchestrationVersion, context.Trigger,
                    blackboard = Summarize(context.Blackboard), context.CompletedNodeTypes,
                    skills = context.Skills, capabilities = context.Capabilities.Select(x => new { x.Id, x.NodeType, x.Description, x.MaxOccurrences }),
                    budget = new { maxRetrievalIterations = 2, maxDynamicNodes = ResearchQualityReviewWorkflow.MaxDynamicNodes }, validationError = error
                }, Json), .1, 3000, ChatResponseFormat.JsonObject), timeout.Token);
                var finished = await Task.WhenAny(completion, Task.Delay(TimeSpan.FromSeconds(45), cancellationToken));
                if (finished != completion)
                {
                    timeout.Cancel(); error = "Workflow planner timed out after 45 seconds."; break;
                }
                var response = await completion;
                var parsed = Parse(response.Content, context, attempt == 0 ? "Llm" : "LlmRepair", response);
                return parsed with { Provider = chat.Provider };
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { error = ex.Message; }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { error = "Workflow planner timed out after 45 seconds."; break; }
        }
        return DeterministicDynamicWorkflowPlanner.Create(context, error, chat.Model);
    }

    private static DynamicPlanProposal Parse(string content, WorkflowPlanningContext context, string mode, ChatCompletionResult response)
    {
        using var doc = JsonDocument.Parse(content); var root = doc.RootElement;
        var goal = root.GetProperty("goalStatus").GetString() ?? DynamicGoalStatuses.Continue;
        var reason = root.GetProperty("reason").GetString() ?? "Planner returned no reason.";
        var skills = root.TryGetProperty("selectedSkills", out var s) ? s.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList() : [];
        var actions = root.TryGetProperty("actions", out var a) ? a.EnumerateArray().Select(x => new DynamicPlanAction(
            x.GetProperty("clientNodeKey").GetString() ?? throw new InvalidOperationException("clientNodeKey is required."),
            x.GetProperty("capability").GetString() ?? throw new InvalidOperationException("capability is required."),
            x.GetProperty("nodeType").GetString() ?? throw new InvalidOperationException("nodeType is required."),
            x.TryGetProperty("dependsOn", out var d) ? d.EnumerateArray().Select(y => y.GetString() ?? "").ToList() : [],
            JsonNode.Parse(x.TryGetProperty("arguments", out var args) ? args.GetRawText() : "{}")!.AsObject(),
            x.TryGetProperty("condition", out var condition) && condition.ValueKind == JsonValueKind.Object ? new DynamicPlanCondition(condition.GetProperty("path").GetString() ?? "", condition.GetProperty("equals").GetString() ?? "") : null,
            x.TryGetProperty("iteration", out var i) ? i.GetInt32() : 0)).ToList() : [];
        return new(Guid.NewGuid(), context.OrchestrationVersion, context.Trigger, goal, reason, skills, actions, mode, response.Model, response.PromptTokens, response.CompletionTokens);
    }
    private static object Summarize(JsonObject board) => new { question = board[AgentBlackboardKeys.Question], criticReview = board[AgentBlackboardKeys.CriticReview], unresolvedClaims = board[AgentBlackboardKeys.UnresolvedClaims], routeDecision = board[AgentBlackboardKeys.RouteDecision], requiresReanalysis = board[AgentBlackboardKeys.RequiresReanalysis], reanalysisReasons = board[AgentBlackboardKeys.ReanalysisReasons] };
    private const string SystemPrompt = """
You are the constrained workflow planner for an equity research quality run. Return one JSON object only. Select only supplied skills, capabilities and nodeTypes. Never output providers, tools, code, raw Blackboard writes, unknown nodes, or graph cycles. Decide high-level research intent; retrieval nodes own concrete tool calls. Output {"goalStatus":"Continue|Complete","reason":"...","selectedSkills":["..."],"actions":[{"clientNodeKey":"unique-key","capability":"...","nodeType":"...","dependsOn":["client-key-or-existing-node-key"],"iteration":0,"arguments":{}}]}. Every action requires at least one dependency. If evidence is missing, provide retrieval arguments with 1-3 searchIntents containing targetClaims as an array, topic, preferredSourceRoles as an array, freshness as day|week|month|year and topK. Select RetrieveWebEvidence only for freshness or local evidence gaps. Dynamic local retrieval must set allowWebFallback=false. Respect all supplied budgets, including at most one Web retrieval node.
""";
}

public static class DeterministicDynamicWorkflowPlanner
{
    public static DynamicPlanProposal Create(WorkflowPlanningContext c, string? fallbackReason = null, string? model = null)
    {
        var board = c.Blackboard; var review = board[AgentBlackboardKeys.CriticReview] as JsonObject;
        var requiresEvidence = review?[CriticReviewFields.RequiresMoreEvidence]?.GetValue<bool>() == true;
        var requiresRevision = review?[CriticReviewFields.RequiresRevision]?.GetValue<bool>() == true;
        IReadOnlyList<DynamicPlanAction> actions; IReadOnlyList<string> skills; string goal; string reason;
        if (c.CompletedNodeTypes.Any(x => x is DraftRevisionNodeTypes.FinalizeRevision or EvidenceRemediationNodeTypes.Finalize or EvidenceReanalysisNodeTypes.Finalize))
        { actions = []; skills = ["quality-finalization"]; goal = DynamicGoalStatuses.Complete; reason = "The planned finalization node completed."; }
        else if (c.Trigger == DynamicPlanningTriggers.CriticCompleted && requiresEvidence)
        {
            var iteration = Math.Min(c.RetrievalIterations + 1, 2); var suffix = $":{iteration}";
            actions = Chain([
                A("extractClaims" + suffix, "extract-claims", EvidenceRemediationNodeTypes.ExtractClaims, iteration: iteration),
                A("retrieveEvidence" + suffix, "retrieve-primary-financial-evidence", EvidenceRemediationNodeTypes.RetrieveEvidence, Args(board), iteration),
                A("assessEvidence" + suffix, "assess-claim-evidence", EvidenceRemediationNodeTypes.AssessSupport, iteration: iteration),
                A("validateEvidence" + suffix, "validate-evidence", EvidenceRemediationNodeTypes.ValidateMappings, iteration: iteration),
                A("routeEvidence" + suffix, "route-evidence", EvidenceRemediationNodeTypes.Route, iteration: iteration)
            ], ResearchQualityReviewNodeKeys.FinalizeCriticReport); skills = ["evidence-remediation", "financial-guidance-verification"]; goal = DynamicGoalStatuses.Continue; reason = "Critic requires additional evidence.";
        }
        else if (c.Trigger != DynamicPlanningTriggers.CriticCompleted && board[AgentBlackboardKeys.RouteDecision]?.GetValue<string>() == "InsufficientEvidence" && c.RetrievalIterations < 2)
        {
            var iteration = c.RetrievalIterations + 1; var suffix = $":{iteration}";
            var webUsed = c.CompletedNodeTypes.Contains(EvidenceRemediationNodeTypes.RetrieveWebEvidence);
            var retrieval = webUsed
                ? A("retrieveEvidence" + suffix, "retrieve-primary-financial-evidence", EvidenceRemediationNodeTypes.RetrieveEvidence, Args(board), iteration)
                : A("retrieveWebEvidence" + suffix, "retrieve-web-evidence", EvidenceRemediationNodeTypes.RetrieveWebEvidence, Args(board, false), iteration);
            actions = Chain([retrieval, A("assessEvidence" + suffix, "assess-claim-evidence", EvidenceRemediationNodeTypes.AssessSupport, iteration: iteration), A("validateEvidence" + suffix, "validate-evidence", EvidenceRemediationNodeTypes.ValidateMappings, iteration: iteration), A("routeEvidence" + suffix, "route-evidence", EvidenceRemediationNodeTypes.Route, iteration: iteration)], Last(c));
            skills = ["evidence-remediation"]; goal = DynamicGoalStatuses.Continue; reason = "Evidence remains insufficient and one retrieval iteration remains.";
        }
        else if (c.Trigger != DynamicPlanningTriggers.CriticCompleted && board[AgentBlackboardKeys.RequiresReanalysis]?.GetValue<bool>() == true)
        {
            actions = Chain([A("buildRemediatedPacket", "build-evidence-packet", EvidenceRemediationNodeTypes.BuildPacket), A("buildAnalysisContext", "build-analysis-context", EvidenceReanalysisNodeTypes.BuildContext), A("reanalyzeAnswer", "reanalyze-investment-answer", EvidenceReanalysisNodeTypes.Reanalyze), A("critiqueReanalysis", "critique-reanalysis", EvidenceReanalysisNodeTypes.Critique), A("reviseReanalysis", "revise-reanalysis", EvidenceReanalysisNodeTypes.Revise), A("finalizeReanalysis", "finalize-reanalysis", EvidenceReanalysisNodeTypes.Finalize)], Last(c));
            skills = ["evidence-driven-reanalysis"]; goal = DynamicGoalStatuses.Continue; reason = "Validated evidence materially changes the analysis.";
        }
        else if (c.Trigger != DynamicPlanningTriggers.CriticCompleted)
        {
            actions = Chain([A("buildRemediatedPacket", "build-evidence-packet", EvidenceRemediationNodeTypes.BuildPacket), A("draftEvidenceRevision", "revise-with-evidence", EvidenceRemediationNodeTypes.DraftRevision), A("finalizeQuality", "finalize-quality", EvidenceRemediationNodeTypes.Finalize)], Last(c));
            skills = ["evidence-remediation", "quality-finalization"]; goal = DynamicGoalStatuses.Continue; reason = c.RetrievalIterations >= 2 ? "Retrieval budget exhausted; finalize with explicit insufficiency." : "Validated evidence is ready for revision.";
        }
        else if (requiresRevision)
        {
            actions = Chain([A("draftRevisedAnswer", "revise-answer", DraftRevisionNodeTypes.DraftRevisedAnswer), A("finalizeRevision", "finalize-revision", DraftRevisionNodeTypes.FinalizeRevision)], ResearchQualityReviewNodeKeys.FinalizeCriticReport); skills = ["answer-revision"]; goal = DynamicGoalStatuses.Continue; reason = "Critic requires a bounded answer revision.";
        }
        else { actions = []; skills = ["quality-finalization"]; goal = DynamicGoalStatuses.Complete; reason = "Critic accepted the answer."; }
        return new(Guid.NewGuid(), c.OrchestrationVersion, c.Trigger, goal, reason, skills, actions, "DeterministicFallback", model, 0, 0, fallbackReason);
    }
    private static DynamicPlanAction A(string key, string capability, string type, JsonObject? args = null, int iteration = 0) => new(key, capability, type, [], args ?? new JsonObject(), null, iteration);
    private static IReadOnlyList<DynamicPlanAction> Chain(IReadOnlyList<DynamicPlanAction> actions, string predecessor)
    { var result = new List<DynamicPlanAction>(); var previous = predecessor; foreach (var action in actions) { result.Add(action with { DependsOn = [previous] }); previous = action.ClientNodeKey; } return result; }
    private static string Last(WorkflowPlanningContext c) => c.Blackboard["dynamicLastNodeKey"]?.GetValue<string>() ?? ResearchQualityReviewNodeKeys.FinalizeCriticReport;
    private static JsonObject Args(JsonObject board, bool local = true) => new() { ["allowWebFallback"] = local ? false : null, ["searchIntents"] = new JsonArray { new JsonObject { ["targetClaims"] = board[AgentBlackboardKeys.UnresolvedClaims]?.DeepClone() ?? new JsonArray(), ["topic"] = board[AgentBlackboardKeys.Question]?.DeepClone(), ["preferredSourceRoles"] = new JsonArray("Primary"), ["freshness"] = "year", ["topK"] = local ? 6 : 5 } } };
}
