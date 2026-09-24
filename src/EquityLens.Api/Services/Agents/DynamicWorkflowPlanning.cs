using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Ai;

namespace EquityLens.Api.Services.Agents;

public static class DynamicGoalStatuses { public const string Continue = "Continue"; public const string Complete = "Complete"; }
public static class DynamicPlanningTriggers
{
    public const string ResearchContextReady = "ResearchContextReady";
    public const string CriticCompleted = "CriticCompleted";
    public const string BranchCompleted = "BranchCompleted";
    public const string EvidenceValidated = "EvidenceValidated";
    public const string FeedbackContextReady = "FeedbackContextReady";
    public const string CapabilityRequestsReady = "CapabilityRequestsReady";
}

public static class WorkflowSkillKinds
{
    public const string Lead = "Lead";
    public const string Supporting = "Supporting";
}

public sealed record WorkflowSkill(string Id, string Description, IReadOnlyList<string> Capabilities)
{
    public string DisplayName { get; init; } = Id;
    public string Kind { get; init; } = WorkflowSkillKinds.Supporting;
    public IReadOnlyList<string> SupportedWorkflowTypes { get; init; } = [];
    public bool Routable { get; init; }
    public string? PromptTemplateId { get; init; }
    public int? PromptVersion { get; init; }
    public IReadOnlyList<string> RequiredInputs { get; init; } = [];
    public string? SystemPrompt { get; init; }
}
public interface IWorkflowSkillCatalog { IReadOnlyList<WorkflowSkill> Skills { get; } }
public sealed class WorkflowSkillCatalog : IWorkflowSkillCatalog
{
    public IReadOnlyList<WorkflowSkill> Skills { get; } =
    [
        new("conference-call-takeaways", "Distill conference-call evidence into guidance changes, management tone shifts and thesis implications.", ["plan-research-retrieval", "retrieve-local-research-evidence", "evaluate-initial-evidence", "retrieve-web-research-evidence", "rank-research-evidence", "draft-research-answer", "build-initial-evidence-packet", "check-initial-evidence", "critique-initial-answer", "finalize-initial-critic"])
        {
            DisplayName = "法說會要點蒸餾",
            Kind = WorkflowSkillKinds.Lead,
            SupportedWorkflowTypes = [AgentWorkflowTypes.ResearchInvestigation],
            Routable = true,
            PromptTemplateId = "conference-call-takeaways",
            PromptVersion = 1,
            RequiredInputs = ["security", "conference transcript or notes", "reporting period or event date", "depth"],
            SystemPrompt = InvestmentResearchSkillPrompts.ConferenceCallTakeaways
        },
        new("research-investigation", "Plan an evidence-grounded research answer from the validated request.", ["plan-research-retrieval", "retrieve-local-research-evidence", "evaluate-initial-evidence", "retrieve-web-research-evidence", "rank-research-evidence", "prepare-portfolio-risk-math-inputs", ..PortfolioRiskMathCapabilities.All.Select(x => x.Id), "draft-research-answer", "build-initial-evidence-packet", "check-initial-evidence", "critique-initial-answer", "finalize-initial-critic"])
        {
            DisplayName = "一般個股研究",
            Kind = WorkflowSkillKinds.Lead,
            SupportedWorkflowTypes = [AgentWorkflowTypes.ResearchInvestigation],
            Routable = true
        },
        new("portfolio-risk-summary", "Summarize portfolio concentration, drawdown, volatility, VaR, attribution and portfolio health.", ["prepare-portfolio-risk-math-inputs", ..PortfolioRiskMathCapabilities.All.Select(x => x.Id), "evaluate-portfolio-diagnosis-quality", "build-portfolio-evidence-packet", "draft-portfolio-diagnosis", "finalize-portfolio-diagnosis"])
        {
            DisplayName = "組合風險摘要",
            Kind = WorkflowSkillKinds.Lead,
            SupportedWorkflowTypes = [AgentWorkflowTypes.PortfolioDiagnosis],
            Routable = true,
            RequiredInputs = ["portfolio"]
        },
        new("current-market-event-investigation", "Investigate date-sensitive price moves, news and current market events with Web evidence.", ["plan-research-retrieval", "retrieve-local-research-evidence", "evaluate-initial-evidence", "retrieve-web-research-evidence", "rank-research-evidence", "draft-research-answer", "build-initial-evidence-packet", "check-initial-evidence", "critique-initial-answer", "finalize-initial-critic"]),
        new("evidence-remediation", "Close citation and evidence gaps with bounded local or Web retrieval, validation, and revision.", ["extract-claims", "retrieve-primary-financial-evidence", "retrieve-web-evidence", "assess-claim-evidence", "validate-evidence", "build-evidence-packet", "revise-with-evidence", "finalize-quality"]),
        new("financial-guidance-verification", "Verify financial guidance with primary or supporting evidence.", ["retrieve-primary-financial-evidence", "assess-claim-evidence", "validate-evidence"]),
        new("evidence-driven-reanalysis", "Reanalyze material conclusions using validated evidence only.", ["build-analysis-context", "reanalyze-investment-answer", "critique-reanalysis", "revise-reanalysis", "finalize-reanalysis"]),
        new("answer-revision", "Revise wording or conclusions from critic findings.", ["revise-answer", "finalize-revision"]),
        new("quality-finalization", "Finish the quality workflow without adding unsupported work.", ["finalize-quality"]),
        new("feedback-driven-revision", "Revise a completed research answer from explicit user feedback using the smallest validated DAG.", ["plan-research-retrieval", "retrieve-local-research-evidence", "evaluate-initial-evidence", "retrieve-web-research-evidence", "rank-research-evidence", "draft-research-answer", "build-initial-evidence-packet", "check-initial-evidence", "critique-initial-answer", "finalize-initial-critic", "extract-claims", "retrieve-primary-financial-evidence", "retrieve-web-evidence", "assess-claim-evidence", "validate-evidence", "build-evidence-packet", "revise-with-evidence", "build-analysis-context", "reanalyze-investment-answer", "critique-reanalysis", "revise-reanalysis", "finalize-reanalysis"]),
        new("portfolio-risk-mathematics", "Run deterministic portfolio and risk mathematics from provenance-backed Blackboard inputs.", ["prepare-portfolio-risk-math-inputs", ..PortfolioRiskMathCapabilities.All.Select(x => x.Id)])
    ];
}

public sealed record NodeCapability(string Id, string NodeType, string Description, string ArgumentSchema, IReadOnlyList<string> RequiredKeys, IReadOnlyList<string> ProducedKeys, string SideEffectLevel, bool Idempotent, bool SupportsLoop, int MaxOccurrences, JsonObject? ParametersSchema = null, string InputMode = "Any");
public interface INodeCapabilityRegistry { IReadOnlyList<NodeCapability> Capabilities { get; } NodeCapability Get(string id); }
public sealed class NodeCapabilityRegistry : INodeCapabilityRegistry
{
    public IReadOnlyList<NodeCapability> Capabilities { get; } =
    [
        C("plan-research-retrieval", ResearchInvestigationNodeTypes.PlanRetrieval, "Build the initial retrieval strategy."),
        C("retrieve-local-research-evidence", ResearchInvestigationNodeTypes.RetrieveLocal, "Retrieve local filing and research evidence."),
        C("evaluate-initial-evidence", ResearchInvestigationNodeTypes.EvaluateEvidence, "Evaluate source policy and freshness requirements."),
        C("retrieve-web-research-evidence", ResearchInvestigationNodeTypes.RetrieveWeb, "Retrieve current Web evidence when permitted and required."),
        C("rank-research-evidence", ResearchInvestigationNodeTypes.RankEvidence, "Rank and select initial research evidence."),
        C("draft-research-answer", ResearchInvestigationNodeTypes.DraftAnswer, "Draft the evidence-grounded research answer."),
        C("build-initial-evidence-packet", ResearchQualityReviewNodeTypes.BuildEvidencePacket, "Build the initial answer evidence packet."),
        C("check-initial-evidence", ResearchQualityReviewNodeTypes.CheckEvidence, "Check initial evidence coverage."),
        C("critique-initial-answer", ResearchQualityReviewNodeTypes.CritiqueAnswer, "Critique the initial answer."),
        C("finalize-initial-critic", ResearchQualityReviewNodeTypes.FinalizeCriticReport, "Finalize the initial critic decision."),
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
        C("finalize-reanalysis", EvidenceReanalysisNodeTypes.Finalize, "Finalize the reanalysis result."),
        C("evaluate-portfolio-diagnosis-quality", PortfolioDiagnosisNodeTypes.EvaluateQuality, "Deterministically evaluate portfolio diagnosis completeness.", loop: true, max: 2),
        C("build-portfolio-evidence-packet", PortfolioDiagnosisNodeTypes.BuildEvidencePacket, "Build the final portfolio evidence packet."),
        C("draft-portfolio-diagnosis", PortfolioDiagnosisNodeTypes.DraftDiagnosis, "Draft the portfolio diagnosis from validated evidence."),
        C("finalize-portfolio-diagnosis", PortfolioDiagnosisNodeTypes.FinalizeDiagnosis, "Publish the portfolio diagnosis after approval."),
        new("prepare-portfolio-risk-math-inputs", PortfolioRiskMathNodeTypes.PrepareInputs, "Load authorized market and portfolio data into provenance-backed Blackboard math inputs.", "PreparePortfolioRiskMathInputs", [], [AgentBlackboardKeys.MathInputs], "ReadOnly", true, false, 1, new JsonObject { ["type"] = "object", ["additionalProperties"] = false }, "Any"),
        ..PortfolioRiskMathCapabilities.All
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
public sealed record WorkflowPlanningContext(Guid RunId, long OrchestrationVersion, string Trigger, JsonObject Blackboard, IReadOnlyList<string> CompletedNodeTypes, IReadOnlyList<WorkflowSkill> Skills, IReadOnlyList<NodeCapability> Capabilities, int RetrievalIterations, int DynamicNodeCount, string? PlanningAnchorNodeKey = null, AgentLoopDecision? LoopDecision = null);
public interface IAgentWorkflowPlanner { Task<DynamicPlanProposal> PlanAsync(WorkflowPlanningContext context, CancellationToken cancellationToken = default); }

public sealed class LlmAgentWorkflowPlanner(IChatCompletionService chat) : IAgentWorkflowPlanner
{
    public const string PromptTemplateId = "research-quality-workflow-planner";
    public const int PromptVersion = 3;
    public const int TimeoutSeconds = 15;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<DynamicPlanProposal> PlanAsync(WorkflowPlanningContext context, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
        string? error = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var completion = chat.CompleteAsync(new ChatCompletionRequest(SystemPrompt, JsonSerializer.Serialize(new
                {
                    context.RunId, context.OrchestrationVersion, context.Trigger, context.PlanningAnchorNodeKey, context.LoopDecision,
                    blackboard = Summarize(context.Blackboard), context.CompletedNodeTypes,
                    skills = context.Skills,
                    tools = VisibleCapabilities(context).Select(x => new
                    {
                        name = x.Id, x.NodeType, x.Description, parameters = x.ParametersSchema,
                        requiresBlackboard = x.RequiredKeys, producesBlackboard = x.ProducedKeys,
                        x.InputMode, x.SideEffectLevel, x.MaxOccurrences
                    }),
                    budget = new { maxRetrievalIterations = 2, maxDynamicNodes = ResearchQualityReviewWorkflow.MaxDynamicNodes }, validationError = error
                }, Json), .1, 3000, ChatResponseFormat.JsonObject), timeout.Token);
                var finished = await Task.WhenAny(completion, Task.Delay(TimeSpan.FromSeconds(TimeoutSeconds), cancellationToken));
                if (finished != completion)
                {
                    timeout.Cancel(); error = $"Workflow planner timed out after {TimeoutSeconds} seconds."; break;
                }
                var response = await completion;
                var parsed = Parse(response.Content, context, attempt == 0 ? "Llm" : "LlmRepair", response);
                return parsed with { Provider = chat.Provider };
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { error = ex.Message; }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { error = $"Workflow planner timed out after {TimeoutSeconds} seconds."; break; }
        }
        return DeterministicDynamicWorkflowPlanner.Create(context, error, chat.Model);
    }

    private static DynamicPlanProposal Parse(string content, WorkflowPlanningContext context, string mode, ChatCompletionResult response)
    {
        using var doc = JsonDocument.Parse(content); var root = doc.RootElement;
        var goal = root.GetProperty("goalStatus").GetString() ?? DynamicGoalStatuses.Continue;
        var reason = root.GetProperty("reason").GetString() ?? "Planner returned no reason.";
        if (!ContainsCjk(reason) || ContainsSimplifiedChineseMarker(reason))
            throw new InvalidOperationException("Planner reason must use Traditional Chinese.");
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
    private static bool ContainsCjk(string value) => value.Any(x => x is >= '\u3400' and <= '\u9fff');
    private static bool ContainsSimplifiedChineseMarker(string value) =>
        value.IndexOfAny("发为会这与后国语变从对个们业产当应还进过数资实据".ToCharArray()) >= 0;
    private static object Summarize(JsonObject board) => new { question = board[AgentBlackboardKeys.Question], researchRequest = board[AgentBlackboardKeys.ResearchRequest], researchIntent = board[AgentBlackboardKeys.ResearchIntent], leadSkill = board[AgentBlackboardKeys.LeadSkill], routingContext = board[AgentBlackboardKeys.RoutingContext], initialEvidencePolicy = board[AgentBlackboardKeys.InitialEvidencePolicy], capabilityRequestAssessment = board[AgentBlackboardKeys.CapabilityRequestAssessment], capabilityRequests = board[AgentBlackboardKeys.CapabilityRequests], revisionContext = board[AgentBlackboardKeys.RevisionContext], feedbackIntent = board[AgentBlackboardKeys.FeedbackIntent], plannerValidationError = board["plannerValidationError"], criticReview = board[AgentBlackboardKeys.CriticReview], unresolvedClaims = board[AgentBlackboardKeys.UnresolvedClaims], requiredResearchDimensions = board[AgentBlackboardKeys.RequiredResearchDimensions], missingResearchDimensions = board[AgentBlackboardKeys.MissingResearchDimensions], routeDecision = board[AgentBlackboardKeys.RouteDecision], requiresReanalysis = board[AgentBlackboardKeys.RequiresReanalysis], reanalysisReasons = board[AgentBlackboardKeys.ReanalysisReasons] };
    private static IEnumerable<NodeCapability> VisibleCapabilities(WorkflowPlanningContext context)
    {
        var request = context.Blackboard[AgentBlackboardKeys.ResearchRequest]?.Deserialize<ResearchAskRequest>(AgentNodeJson.SerializerOptions);
        var hasPortfolio = request?.PortfolioId is not null || context.Blackboard[AgentBlackboardKeys.PortfolioId] is not null;
        return context.Capabilities.Where(x => x.NodeType != PortfolioRiskMathNodeTypes.Execute || hasPortfolio || x.InputMode is "SingleAsset" or "Any");
    }
    private const string SystemPrompt = """
You are the constrained supervisor planner for an equity research run. Return one JSON object only. Select only supplied skills, tools and nodeTypes. A tool is a capability that becomes a backend workflow node after validation; you do not execute tools directly. Never output providers, code, raw Blackboard writes, unknown nodes, or graph cycles. Output {"goalStatus":"Continue|Complete","reason":"...","selectedSkills":["..."],"actions":[{"clientNodeKey":"unique-key","capability":"...","nodeType":"...","dependsOn":["client-key-or-existing-node-key"],"iteration":0,"arguments":{}}]}. The reason field MUST be written in Traditional Chinese and include the original ticker when one is available. Every other Chinese string value, including searchIntents, must also use Traditional Chinese. Preserve company names and tickers exactly as supplied in Blackboard; never transliterate or convert them to Simplified Chinese. Every action requires at least one dependency. The first action must depend on PlanningAnchorNodeKey exactly; every later action must depend on a preceding action key. For conference-call-takeaways with Auto or LocalThenWeb, ResearchContextReady must stop after PlanResearchRetrieval, RetrieveLocalResearchEvidence and EvaluateInitialEvidencePolicy. On CapabilityRequestsReady, add RetrieveWebResearchEvidence only when capabilityRequests contains a Pending matching request, then complete the branch through Rank, Draft, evidence checks, Critic and Finalize. Without a Pending request, continue without Web. Math tools may receive only bounded configuration parameters described by their schema; never provide prices, returns, holdings, weights, covariance matrices, or other numeric source data because those must come from Blackboard. Select math tools only when the user explicitly needs a calculation, risk measure, simulation or portfolio comparison, with at most eight math actions and no duplicate math capability. On other ResearchContextReady requests, build a complete initial research branch ending in FinalizeCriticReport. On FeedbackContextReady, use feedbackIntent and revisionContext to build the smallest sufficient revision DAG. Respect sourcePolicy: LocalOnly forbids Web, WebOnly forbids actual local retrieval, LocalAndWeb requires Web. Retrieval nodes own concrete tool calls. Respect all budgets, including at most one Web retrieval node.
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
        if (c.LoopDecision is not null)
            return CreateLoopProposal(c, fallbackReason, model);
        if (c.Trigger == DynamicPlanningTriggers.ResearchContextReady)
        {
            var request = board[AgentBlackboardKeys.ResearchRequest]?.Deserialize<ResearchAskRequest>(AgentNodeJson.SerializerOptions)
                ?? new ResearchAskRequest(string.Empty, board[AgentBlackboardKeys.Question]?.GetValue<string>() ?? string.Empty);
            var includeWeb = request.SourcePolicy is SourcePolicy.WebOnly or SourcePolicy.LocalAndWeb
                || request.SourcePolicy == SourcePolicy.Auto && ResearchInvestigationPlanning.IsFreshnessSensitive(request.Question)
                || request.SourcePolicy == SourcePolicy.LocalThenWeb;
            var capabilityGate = IsConferenceCapabilityGate(board, request);
            var planned = new List<DynamicPlanAction>
            {
                A("planResearchRetrieval:1", "plan-research-retrieval", ResearchInvestigationNodeTypes.PlanRetrieval)
            };
            if (request.SourcePolicy != SourcePolicy.WebOnly) planned.Add(A("retrieveLocalResearchEvidence:1", "retrieve-local-research-evidence", ResearchInvestigationNodeTypes.RetrieveLocal));
            planned.Add(A("evaluateInitialEvidencePolicy:1", "evaluate-initial-evidence", ResearchInvestigationNodeTypes.EvaluateEvidence));
            if (!capabilityGate && includeWeb) planned.Add(A("retrieveWebResearchEvidence:1", "retrieve-web-research-evidence", ResearchInvestigationNodeTypes.RetrieveWeb));
            if (!capabilityGate)
            {
            planned.AddRange([
                A("rankAndSelectResearchEvidence:1", "rank-research-evidence", ResearchInvestigationNodeTypes.RankEvidence),
            ]);
            if (NeedsMath(request.Question, out var mathCapability))
            {
                planned.Add(A("preparePortfolioRiskMathInputs:1", "prepare-portfolio-risk-math-inputs", PortfolioRiskMathNodeTypes.PrepareInputs));
                planned.Add(A($"{mathCapability}:1", mathCapability, PortfolioRiskMathNodeTypes.Execute));
            }
            planned.AddRange([
                A("draftResearchAnswer:1", "draft-research-answer", ResearchInvestigationNodeTypes.DraftAnswer),
                A("buildEvidencePacket:1", "build-initial-evidence-packet", ResearchQualityReviewNodeTypes.BuildEvidencePacket),
                A("checkEvidence:1", "check-initial-evidence", ResearchQualityReviewNodeTypes.CheckEvidence),
                A("critiqueAnswer:1", "critique-initial-answer", ResearchQualityReviewNodeTypes.CritiqueAnswer),
                A("finalizeCriticReport:1", "finalize-initial-critic", ResearchQualityReviewNodeTypes.FinalizeCriticReport)
            ]);
            }
            actions = Chain(planned, ResearchInvestigationNodeKeys.DetectIntent);
            var routedLeadSkill = board[AgentBlackboardKeys.LeadSkill]?.GetValue<string>();
            skills = !string.IsNullOrWhiteSpace(routedLeadSkill)
                ? [routedLeadSkill]
                : includeWeb ? ["research-investigation", "current-market-event-investigation"] : ["research-investigation"];
            if (string.IsNullOrWhiteSpace(routedLeadSkill) && planned.Any(x => x.NodeType == PortfolioRiskMathNodeTypes.Execute))
                skills = [..skills, "portfolio-risk-mathematics"];
            goal = DynamicGoalStatuses.Continue;
            reason = capabilityGate ? "先取得本地證據，再由 Node Agent 提出能力需求供 Planner 審核。" : includeWeb ? "研究請求需要包含 Web 檢索的初始證據分支。" : "研究請求可先使用本地證據建立初始分支。";
        }
        else if (c.Trigger == DynamicPlanningTriggers.CapabilityRequestsReady)
        {
            var pendingWeb = PendingWebRequest(board) is not null;
            var planned = new List<DynamicPlanAction>();
            if (pendingWeb) planned.Add(A("retrieveWebResearchEvidence:approved", "retrieve-web-research-evidence", ResearchInvestigationNodeTypes.RetrieveWeb));
            planned.AddRange([
                A("rankAndSelectResearchEvidence:approved", "rank-research-evidence", ResearchInvestigationNodeTypes.RankEvidence),
                A("draftResearchAnswer:approved", "draft-research-answer", ResearchInvestigationNodeTypes.DraftAnswer),
                A("buildEvidencePacket:approved", "build-initial-evidence-packet", ResearchQualityReviewNodeTypes.BuildEvidencePacket),
                A("checkEvidence:approved", "check-initial-evidence", ResearchQualityReviewNodeTypes.CheckEvidence),
                A("critiqueAnswer:approved", "critique-initial-answer", ResearchQualityReviewNodeTypes.CritiqueAnswer),
                A("finalizeCriticReport:approved", "finalize-initial-critic", ResearchQualityReviewNodeTypes.FinalizeCriticReport)
            ]);
            actions = Chain(planned, c.PlanningAnchorNodeKey ?? Last(c));
            skills = ["conference-call-takeaways"];
            goal = DynamicGoalStatuses.Continue;
            reason = pendingWeb ? "Planner 核准 Node Agent 提出的 Web 檢索能力需求。" : "Node Agent 未提出 Web 能力需求，使用本地證據繼續研究流程。";
        }
        else if (c.Trigger == DynamicPlanningTriggers.FeedbackContextReady)
        {
            var request = board[AgentBlackboardKeys.ResearchRequest]?.Deserialize<ResearchAskRequest>(AgentNodeJson.SerializerOptions)
                ?? new ResearchAskRequest(board[AgentBlackboardKeys.Ticker]?.GetValue<string>() ?? string.Empty, board[AgentBlackboardKeys.Question]?.GetValue<string>() ?? string.Empty);
            var feedback = board[AgentBlackboardKeys.FeedbackComment]?.GetValue<string>() ?? string.Empty;
            var intent = board[AgentBlackboardKeys.FeedbackIntent]?.GetValue<string>() ?? "Wording";
            var planned = new List<DynamicPlanAction>();
            if (intent != "Wording")
            {
                planned.Add(A("planFeedbackRetrieval:1", "plan-research-retrieval", ResearchInvestigationNodeTypes.PlanRetrieval));
                if (request.SourcePolicy != SourcePolicy.WebOnly) planned.Add(A("retrieveFeedbackLocalEvidence:1", "retrieve-local-research-evidence", ResearchInvestigationNodeTypes.RetrieveLocal));
                planned.Add(A("evaluateFeedbackEvidence:1", "evaluate-initial-evidence", ResearchInvestigationNodeTypes.EvaluateEvidence));
                var includeWeb = request.SourcePolicy is SourcePolicy.WebOnly or SourcePolicy.LocalAndWeb or SourcePolicy.LocalThenWeb
                    || request.SourcePolicy == SourcePolicy.Auto && (ResearchInvestigationPlanning.IsFreshnessSensitive(feedback) || feedback.Contains("最新", StringComparison.OrdinalIgnoreCase));
                if (includeWeb) planned.Add(A("retrieveFeedbackWebEvidence:1", "retrieve-web-research-evidence", ResearchInvestigationNodeTypes.RetrieveWeb));
                planned.Add(A("rankFeedbackEvidence:1", "rank-research-evidence", ResearchInvestigationNodeTypes.RankEvidence));
            }
            if (intent == "Calculation" && NeedsMath(feedback, out var feedbackMath))
            {
                planned.Add(A("prepareFeedbackMathInputs:1", "prepare-portfolio-risk-math-inputs", PortfolioRiskMathNodeTypes.PrepareInputs));
                planned.Add(A($"{feedbackMath}:feedback", feedbackMath, PortfolioRiskMathNodeTypes.Execute));
            }
            planned.AddRange([
                A("draftFeedbackRevision:1", "draft-research-answer", ResearchInvestigationNodeTypes.DraftAnswer),
                A("buildFeedbackEvidencePacket:1", "build-initial-evidence-packet", ResearchQualityReviewNodeTypes.BuildEvidencePacket),
                A("checkFeedbackEvidence:1", "check-initial-evidence", ResearchQualityReviewNodeTypes.CheckEvidence),
                A("critiqueFeedbackRevision:1", "critique-initial-answer", ResearchQualityReviewNodeTypes.CritiqueAnswer),
                A("finalizeFeedbackRevision:1", "finalize-initial-critic", ResearchQualityReviewNodeTypes.FinalizeCriticReport)
            ]);
            actions = Chain(planned, FeedbackRevisionNodeKeys.ValidateContext);
            skills = planned.Any(x => x.NodeType == PortfolioRiskMathNodeTypes.Execute)
                ? ["feedback-driven-revision", "portfolio-risk-mathematics"] : ["feedback-driven-revision"];
            goal = DynamicGoalStatuses.Continue;
            reason = intent == "Wording" ? "使用者回饋可由現有已選證據處理。" : $"使用者回饋需要受限的 {intent} 修訂分支。";
        }
        else if (c.CompletedNodeTypes.Any(x => x is DraftRevisionNodeTypes.FinalizeRevision or EvidenceRemediationNodeTypes.Finalize or EvidenceReanalysisNodeTypes.Finalize))
        { actions = []; skills = ["quality-finalization"]; goal = DynamicGoalStatuses.Complete; reason = "規劃的最終節點已完成。"; }
        else if (c.Trigger == DynamicPlanningTriggers.CriticCompleted && requiresEvidence)
        {
            var iteration = Math.Min(c.RetrievalIterations + 1, 2); var suffix = $":{iteration}";
            var planned = new List<DynamicPlanAction> {
                A("extractClaims" + suffix, "extract-claims", EvidenceRemediationNodeTypes.ExtractClaims, iteration: iteration),
                A("retrieveEvidence" + suffix, "retrieve-primary-financial-evidence", EvidenceRemediationNodeTypes.RetrieveEvidence, Args(board), iteration),
            };
            var webAlreadyUsed = c.CompletedNodeTypes.Any(x => x is ResearchInvestigationNodeTypes.RetrieveWeb or EvidenceRemediationNodeTypes.RetrieveWebEvidence);
            if (NeedsCurrentWebEvidence(board) && !webAlreadyUsed) planned.Add(A("retrieveWebEvidence" + suffix, "retrieve-web-evidence", EvidenceRemediationNodeTypes.RetrieveWebEvidence, Args(board, false), iteration));
            planned.AddRange([
                A("assessEvidence" + suffix, "assess-claim-evidence", EvidenceRemediationNodeTypes.AssessSupport, iteration: iteration),
                A("validateEvidence" + suffix, "validate-evidence", EvidenceRemediationNodeTypes.ValidateMappings, iteration: iteration),
                A("routeEvidence" + suffix, "route-evidence", EvidenceRemediationNodeTypes.Route, iteration: iteration)
            ]);
            actions = Chain(planned, Last(c)); skills = ["evidence-remediation", "financial-guidance-verification"]; goal = DynamicGoalStatuses.Continue; reason = NeedsCurrentWebEvidence(board) ? "評論結果需要目前的外部證據。" : "評論結果需要額外證據。";
        }
        else if (c.Trigger != DynamicPlanningTriggers.CriticCompleted && board[AgentBlackboardKeys.RequiresReanalysis]?.GetValue<bool>() == true)
        {
            actions = Chain([A("buildRemediatedPacket", "build-evidence-packet", EvidenceRemediationNodeTypes.BuildPacket), A("buildAnalysisContext", "build-analysis-context", EvidenceReanalysisNodeTypes.BuildContext), A("reanalyzeAnswer", "reanalyze-investment-answer", EvidenceReanalysisNodeTypes.Reanalyze), A("critiqueReanalysis", "critique-reanalysis", EvidenceReanalysisNodeTypes.Critique), A("reviseReanalysis", "revise-reanalysis", EvidenceReanalysisNodeTypes.Revise), A("finalizeReanalysis", "finalize-reanalysis", EvidenceReanalysisNodeTypes.Finalize)], Last(c));
            skills = ["evidence-driven-reanalysis"]; goal = DynamicGoalStatuses.Continue; reason = "已驗證證據會實質改變分析結果。";
        }
        else if (c.Trigger != DynamicPlanningTriggers.CriticCompleted && board[AgentBlackboardKeys.RouteDecision]?.GetValue<string>() is "InsufficientEvidence" or "PartiallySupportedNeedsRetrieval" && c.RetrievalIterations < 2)
        {
            var iteration = c.RetrievalIterations + 1; var suffix = $":{iteration}";
            var webUsed = c.CompletedNodeTypes.Any(x => x is ResearchInvestigationNodeTypes.RetrieveWeb or EvidenceRemediationNodeTypes.RetrieveWebEvidence);
            var retrieval = webUsed
                ? A("retrieveEvidence" + suffix, "retrieve-primary-financial-evidence", EvidenceRemediationNodeTypes.RetrieveEvidence, Args(board), iteration)
                : A("retrieveWebEvidence" + suffix, "retrieve-web-evidence", EvidenceRemediationNodeTypes.RetrieveWebEvidence, Args(board, false), iteration);
            actions = Chain([retrieval, A("assessEvidence" + suffix, "assess-claim-evidence", EvidenceRemediationNodeTypes.AssessSupport, iteration: iteration), A("validateEvidence" + suffix, "validate-evidence", EvidenceRemediationNodeTypes.ValidateMappings, iteration: iteration), A("routeEvidence" + suffix, "route-evidence", EvidenceRemediationNodeTypes.Route, iteration: iteration)], Last(c));
            skills = ["evidence-remediation"]; goal = DynamicGoalStatuses.Continue; reason = "證據仍不足，且尚可進行一次檢索迭代。";
        }
        else if (c.Trigger != DynamicPlanningTriggers.CriticCompleted)
        {
            actions = Chain([A("buildRemediatedPacket", "build-evidence-packet", EvidenceRemediationNodeTypes.BuildPacket), A("draftEvidenceRevision", "revise-with-evidence", EvidenceRemediationNodeTypes.DraftRevision), A("finalizeQuality", "finalize-quality", EvidenceRemediationNodeTypes.Finalize)], Last(c));
            skills = ["evidence-remediation", "quality-finalization"]; goal = DynamicGoalStatuses.Continue; reason = c.RetrievalIterations >= 2 ? "檢索預算已用盡，應明確標示證據不足並完成流程。" : "已驗證證據可供修訂答案。";
        }
        else if (requiresRevision)
        {
            actions = Chain([A("draftRevisedAnswer", "revise-answer", DraftRevisionNodeTypes.DraftRevisedAnswer), A("finalizeRevision", "finalize-revision", DraftRevisionNodeTypes.FinalizeRevision)], Last(c)); skills = ["answer-revision"]; goal = DynamicGoalStatuses.Continue; reason = "評論結果需要受限的答案修訂。";
        }
        else { actions = []; skills = ["quality-finalization"]; goal = DynamicGoalStatuses.Complete; reason = "評論已接受目前答案。"; }
        return new(Guid.NewGuid(), c.OrchestrationVersion, c.Trigger, goal, reason, skills, actions, "DeterministicFallback", model, 0, 0, fallbackReason);
    }

    private static DynamicPlanProposal CreateLoopProposal(WorkflowPlanningContext c, string? fallbackReason, string? model)
    {
        var decision = c.LoopDecision!;
        IReadOnlyList<DynamicPlanAction> actions;
        IReadOnlyList<string> skills;
        var anchor = c.PlanningAnchorNodeKey ?? Last(c);
        switch (decision.Action)
        {
            case AgentLoopActions.Complete:
                actions = [];
                skills = ["quality-finalization"];
                break;
            case AgentLoopActions.ReviseAnswer:
                actions = Chain([
                    A($"draftRevisedAnswer:{decision.Iteration}", "revise-answer", DraftRevisionNodeTypes.DraftRevisedAnswer, iteration: decision.Iteration),
                    A($"finalizeRevision:{decision.Iteration}", "finalize-revision", DraftRevisionNodeTypes.FinalizeRevision, iteration: decision.Iteration)
                ], anchor);
                skills = ["answer-revision"];
                break;
            case AgentLoopActions.Reanalyze:
                actions = Chain([
                    A($"buildRemediatedPacket:{decision.Iteration}", "build-evidence-packet", EvidenceRemediationNodeTypes.BuildPacket, iteration: decision.Iteration),
                    A($"buildAnalysisContext:{decision.Iteration}", "build-analysis-context", EvidenceReanalysisNodeTypes.BuildContext, iteration: decision.Iteration),
                    A($"reanalyzeAnswer:{decision.Iteration}", "reanalyze-investment-answer", EvidenceReanalysisNodeTypes.Reanalyze, iteration: decision.Iteration),
                    A($"critiqueReanalysis:{decision.Iteration}", "critique-reanalysis", EvidenceReanalysisNodeTypes.Critique, iteration: decision.Iteration),
                    A($"reviseReanalysis:{decision.Iteration}", "revise-reanalysis", EvidenceReanalysisNodeTypes.Revise, iteration: decision.Iteration),
                    A($"finalizeReanalysis:{decision.Iteration}", "finalize-reanalysis", EvidenceReanalysisNodeTypes.Finalize, iteration: decision.Iteration)
                ], anchor);
                skills = ["evidence-driven-reanalysis"];
                break;
            case AgentLoopActions.FinalizeLimited:
                actions = Chain([
                    A($"buildRemediatedPacket:{decision.Iteration}", "build-evidence-packet", EvidenceRemediationNodeTypes.BuildPacket, iteration: decision.Iteration),
                    A($"draftEvidenceRevision:{decision.Iteration}", "revise-with-evidence", EvidenceRemediationNodeTypes.DraftRevision, iteration: decision.Iteration),
                    A($"finalizeQuality:{decision.Iteration}", "finalize-quality", EvidenceRemediationNodeTypes.Finalize, iteration: decision.Iteration)
                ], anchor);
                skills = ["evidence-remediation", "quality-finalization"];
                break;
            case AgentLoopActions.RunRiskAnalyses:
                var mathActions = (decision.RequiredCapabilities ?? []).Select(capability =>
                    A($"{capability}:{decision.Iteration}", capability, PortfolioRiskMathNodeTypes.Execute, iteration: decision.Iteration)
                    with { DependsOn = [anchor] }).ToList();
                var evaluator = A($"evaluatePortfolioQuality:{decision.Iteration}", "evaluate-portfolio-diagnosis-quality", PortfolioDiagnosisNodeTypes.EvaluateQuality, iteration: decision.Iteration)
                    with { DependsOn = mathActions.Select(x => x.ClientNodeKey).ToList() };
                actions = [..mathActions, evaluator];
                skills = ["portfolio-risk-summary"];
                break;
            case AgentLoopActions.FinalizeDiagnosis:
                actions = Chain([
                    A($"buildPortfolioEvidence:{decision.Iteration}", "build-portfolio-evidence-packet", PortfolioDiagnosisNodeTypes.BuildEvidencePacket, iteration: decision.Iteration),
                    A($"draftPortfolioDiagnosis:{decision.Iteration}", "draft-portfolio-diagnosis", PortfolioDiagnosisNodeTypes.DraftDiagnosis, iteration: decision.Iteration),
                    A($"finalizePortfolioDiagnosis:{decision.Iteration}", "finalize-portfolio-diagnosis", PortfolioDiagnosisNodeTypes.FinalizeDiagnosis, iteration: decision.Iteration)
                ], anchor);
                skills = ["portfolio-risk-summary"];
                break;
            default:
                var iteration = decision.Iteration;
                var planned = new List<DynamicPlanAction>();
                if ((c.Blackboard[AgentBlackboardKeys.ExtractedClaims] as JsonArray)?.Count is null or 0)
                    planned.Add(A($"extractClaims:{iteration}", "extract-claims", EvidenceRemediationNodeTypes.ExtractClaims, iteration: iteration));
                var webCount = c.CompletedNodeTypes.Count(x => x == EvidenceRemediationNodeTypes.RetrieveWebEvidence);
                var request = c.Blackboard[AgentBlackboardKeys.ResearchRequest]?.Deserialize<ResearchAskRequest>(AgentNodeJson.SerializerOptions);
                var sourcePolicy = request?.SourcePolicy ?? SourcePolicy.Auto;
                var sources = SourcePolicyRules.SelectRemediationSources(
                    sourcePolicy,
                    iteration,
                    webCount >= ResearchQualityReviewWorkflow.MaxWebRetrievals,
                    NeedsCurrentWebEvidence(c.Blackboard));
                if (sources.UseLocal)
                    planned.Add(A($"retrieveEvidence:{iteration}", "retrieve-primary-financial-evidence", EvidenceRemediationNodeTypes.RetrieveEvidence, Args(c.Blackboard), iteration));
                if (sources.UseWeb)
                    planned.Add(A($"retrieveWebEvidence:{iteration}", "retrieve-web-evidence", EvidenceRemediationNodeTypes.RetrieveWebEvidence, Args(c.Blackboard, false), iteration));
                if (!sources.UseLocal && !sources.UseWeb)
                {
                    planned.AddRange([
                        A($"buildRemediatedPacket:{iteration}", "build-evidence-packet", EvidenceRemediationNodeTypes.BuildPacket, iteration: iteration),
                        A($"draftEvidenceRevision:{iteration}", "revise-with-evidence", EvidenceRemediationNodeTypes.DraftRevision, iteration: iteration),
                        A($"finalizeQuality:{iteration}", "finalize-quality", EvidenceRemediationNodeTypes.Finalize, iteration: iteration)
                    ]);
                    actions = Chain(planned, anchor);
                    skills = ["evidence-remediation", "quality-finalization"];
                    break;
                }
                planned.AddRange([
                    A($"assessEvidence:{iteration}", "assess-claim-evidence", EvidenceRemediationNodeTypes.AssessSupport, iteration: iteration),
                    A($"validateEvidence:{iteration}", "validate-evidence", EvidenceRemediationNodeTypes.ValidateMappings, iteration: iteration),
                    A($"routeEvidence:{iteration}", "route-evidence", EvidenceRemediationNodeTypes.Route, iteration: iteration)
                ]);
                actions = Chain(planned, anchor);
                skills = ["evidence-remediation", "financial-guidance-verification"];
                break;
        }
        return new(Guid.NewGuid(), c.OrchestrationVersion, c.Trigger,
            decision.IsTerminal ? DynamicGoalStatuses.Complete : DynamicGoalStatuses.Continue,
            decision.Reason, skills, actions, "LoopController", model, 0, 0, fallbackReason);
    }
    internal static bool IsConferenceCapabilityGate(JsonObject board, ResearchAskRequest request) =>
        board[AgentBlackboardKeys.LeadSkill]?.GetValue<string>() == "conference-call-takeaways"
        && request.SourcePolicy is SourcePolicy.Auto or SourcePolicy.LocalThenWeb;
    internal static JsonObject? PendingWebRequest(JsonObject board) =>
        (board[AgentBlackboardKeys.CapabilityRequests] as JsonArray)?
        .OfType<JsonObject>()
        .FirstOrDefault(x => x["capabilityId"]?.GetValue<string>() == "retrieve-web-research-evidence"
            && x["status"]?.GetValue<string>() == "Pending");
    private static DynamicPlanAction A(string key, string capability, string type, JsonObject? args = null, int iteration = 0) => new(key, capability, type, [], args ?? new JsonObject(), null, iteration);
    private static IReadOnlyList<DynamicPlanAction> Chain(IReadOnlyList<DynamicPlanAction> actions, string predecessor)
    { var result = new List<DynamicPlanAction>(); var previous = predecessor; foreach (var action in actions) { result.Add(action with { DependsOn = [previous] }); previous = action.ClientNodeKey; } return result; }
    private static string Last(WorkflowPlanningContext c) => c.Blackboard["dynamicLastNodeKey"]?.GetValue<string>() ?? ResearchQualityReviewNodeKeys.FinalizeCriticReport;
    private static JsonObject Args(JsonObject board, bool local = true) => new() { ["allowWebFallback"] = local ? false : null, ["searchIntents"] = new JsonArray { new JsonObject { ["targetClaims"] = Targets(board), ["topic"] = board[AgentBlackboardKeys.Question]?.DeepClone(), ["preferredSourceRoles"] = new JsonArray("Primary"), ["freshness"] = "year", ["topK"] = local ? 6 : 5 } } };
    private static bool NeedsMath(string question, out string capability)
    {
        var rules = new (string Capability, string[] Terms)[]
        {
            ("calculate-sharpe-ratio", ["sharpe", "夏普"]), ("calculate-max-drawdown", ["drawdown", "回撤"]),
            ("calculate-historical-var", ["var", "風險值"]), ("calculate-expected-shortfall", ["expected shortfall", "cvar", "預期短缺"]),
            ("calculate-annualized-volatility", ["volatility", "波動率"]), ("calculate-return", ["return", "報酬率", "漲幅", "跌幅"])
        };
        var match = rules.FirstOrDefault(rule => rule.Terms.Any(term => question.Contains(term, StringComparison.OrdinalIgnoreCase)));
        capability = match.Capability ?? string.Empty; return capability.Length > 0;
    }
    private static JsonNode Targets(JsonObject board)
    {
        if (board[AgentBlackboardKeys.MissingResearchDimensions] is JsonArray { Count: > 0 } dimensions) return dimensions.DeepClone();
        if (board[AgentBlackboardKeys.UnresolvedClaims] is JsonArray { Count: > 0 } unresolved) return unresolved.DeepClone();
        var question = board[AgentBlackboardKeys.Question]?.GetValue<string>(); return string.IsNullOrWhiteSpace(question) ? new JsonArray() : new JsonArray(question);
    }
    private static bool NeedsCurrentWebEvidence(JsonObject board)
    {
        var question = board[AgentBlackboardKeys.Question]?.GetValue<string>() ?? string.Empty;
        return new[] { "未來", "最新", "展望", "指引", "guidance", "outlook", "forecast", "current", "next year" }.Any(x => question.Contains(x, StringComparison.OrdinalIgnoreCase));
    }
}
