using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Services.Agents;

public static class AgentLoopActions
{
    public const string Complete = "Complete";
    public const string RetrieveEvidence = "RetrieveEvidence";
    public const string ReviseAnswer = "ReviseAnswer";
    public const string Reanalyze = "Reanalyze";
    public const string FinalizeLimited = "FinalizeLimited";
    public const string RunRiskAnalyses = "RunRiskAnalyses";
    public const string FinalizeDiagnosis = "FinalizeDiagnosis";
}

public static class AgentLoopStopReasons
{
    public const string QualityGatePassed = "QUALITY_GATE_PASSED";
    public const string MaxIterations = "MAX_ITERATIONS";
    public const string DynamicNodeLimit = "DYNAMIC_NODE_LIMIT";
    public const string NoProgress = "NO_PROGRESS";
    public const string RevisionCompleted = "REVISION_COMPLETED";
    public const string ReanalysisCompleted = "REANALYSIS_COMPLETED";
    public const string LimitedFinalizationCompleted = "LIMITED_FINALIZATION_COMPLETED";
    public const string PlanValidationFailed = "PLAN_VALIDATION_FAILED";
    public const string RunFailed = "RUN_FAILED";
    public const string DataLimited = "DATA_LIMITED";
    public const string HumanRejected = "HUMAN_REJECTED";
}

public sealed class AgentLoopPlanValidationException(string message, Exception innerException)
    : InvalidOperationException(message, innerException);

public sealed record AgentLoopDecision(
    string Action,
    string ReasonCode,
    string Reason,
    int Iteration,
    bool IsTerminal,
    int EvidenceCount,
    IReadOnlyList<string> UnresolvedClaimIds,
    IReadOnlyList<string>? GapCodes = null,
    IReadOnlyList<string>? RequiredCapabilities = null,
    int CompletedCapabilityCount = 0);

public interface IAgentLoopPolicy
{
    string WorkflowType { get; }
    AgentLoopDecision Evaluate(AgentRun run);
}

public interface IAgentLoopController
{
    AgentLoopDecision? Evaluate(AgentRun run);
}

public sealed class AgentLoopController(IEnumerable<IAgentLoopPolicy> policies) : IAgentLoopController
{
    private readonly IReadOnlyDictionary<string, IAgentLoopPolicy> _policies =
        policies.ToDictionary(x => x.WorkflowType, StringComparer.Ordinal);

    public AgentLoopDecision? Evaluate(AgentRun run) =>
        _policies.TryGetValue(run.WorkflowType, out var policy) ? policy.Evaluate(run) : null;
}

public sealed class ResearchQualityReviewLoopPolicy : IAgentLoopPolicy
{
    public string WorkflowType => AgentWorkflowTypes.ResearchQualityReview;

    public AgentLoopDecision Evaluate(AgentRun run)
    {
        var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        var runtime = board[AgentBlackboardKeys.Runtime] as JsonObject ?? new JsonObject();
        board[AgentBlackboardKeys.Runtime] = runtime;
        var last = run.Nodes.Where(x => x.Status == AgentNodeStatuses.Succeeded)
            .OrderByDescending(x => x.CompletedAtUtc).FirstOrDefault();
        var iteration = Math.Max(runtime["iteration"]?.GetValue<int>() ?? 0,
            run.Nodes.Where(IsRetrievalNode).Select(x => x.Iteration).DefaultIfEmpty(0).Max());
        var evidenceCount = DistinctEvidenceCount(board);
        var unresolved = UnresolvedClaimIds(board);
        var unresolvedFingerprint = string.Join('|', unresolved.Order(StringComparer.Ordinal));
        var dynamicNodeCount = run.Nodes.Count(x => !string.IsNullOrWhiteSpace(x.TemplateNodeKey));

        AgentLoopDecision decision;
        if (last?.NodeType is DraftRevisionNodeTypes.FinalizeRevision)
            decision = Stop(AgentLoopStopReasons.RevisionCompleted, "答案修訂已完成。", iteration, evidenceCount, unresolved);
        else if (last?.NodeType is EvidenceReanalysisNodeTypes.Finalize)
            decision = Stop(AgentLoopStopReasons.ReanalysisCompleted, "證據重分析已完成。", iteration, evidenceCount, unresolved);
        else if (last?.NodeType is EvidenceRemediationNodeTypes.Finalize)
            decision = Stop(runtime["pendingStopReason"]?.GetValue<string>() ?? AgentLoopStopReasons.LimitedFinalizationCompleted,
                "證據修補流程已完成。", iteration, evidenceCount, unresolved);
        else if (last?.NodeType == EvidenceRemediationNodeTypes.Route)
            decision = AfterEvidenceGate(board, runtime, iteration, evidenceCount, unresolved, unresolvedFingerprint, dynamicNodeCount);
        else
            decision = AfterCriticGate(board, runtime, iteration, evidenceCount, unresolved, dynamicNodeCount);

        runtime["iteration"] = decision.Iteration;
        runtime["status"] = decision.IsTerminal ? "Stopped" : "Running";
        runtime["lastAction"] = decision.Action;
        runtime["lastReasonCode"] = decision.ReasonCode;
        if (decision.Action == AgentLoopActions.FinalizeLimited)
            runtime["pendingStopReason"] = decision.ReasonCode;
        runtime["stopReason"] = decision.IsTerminal || decision.Action == AgentLoopActions.FinalizeLimited ? decision.ReasonCode : null;
        if (!decision.IsTerminal && decision.Action == AgentLoopActions.RetrieveEvidence)
        {
            runtime["evidenceBaseline"] = evidenceCount;
            runtime["unresolvedClaimsBaseline"] = unresolvedFingerprint;
        }
        run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);
        return decision;
    }

    private static AgentLoopDecision AfterCriticGate(JsonObject board, JsonObject runtime, int iteration,
        int evidenceCount, IReadOnlyList<string> unresolved, int dynamicNodeCount)
    {
        var review = board[AgentBlackboardKeys.CriticReview] as JsonObject;
        if (review?[CriticReviewFields.RequiresMoreEvidence]?.GetValue<bool>() == true)
        {
            if (dynamicNodeCount + 8 > ResearchQualityReviewWorkflow.MaxDynamicNodes)
                return BudgetLimited(dynamicNodeCount, iteration, evidenceCount, unresolved);
            return new(AgentLoopActions.RetrieveEvidence, "EVIDENCE_GAP", "品質關卡要求補充證據。",
                Math.Max(1, iteration + 1), false, evidenceCount, unresolved);
        }
        if (review?[CriticReviewFields.RequiresRevision]?.GetValue<bool>() == true)
            return new(AgentLoopActions.ReviseAnswer, "REVISION_REQUIRED", "品質關卡要求修訂答案。", iteration, false, evidenceCount, unresolved);
        return Stop(AgentLoopStopReasons.QualityGatePassed, "品質關卡已通過。", iteration, evidenceCount, unresolved);
    }

    private static AgentLoopDecision AfterEvidenceGate(JsonObject board, JsonObject runtime, int iteration,
        int evidenceCount, IReadOnlyList<string> unresolved, string unresolvedFingerprint, int dynamicNodeCount)
    {
        if (board[AgentBlackboardKeys.RequiresReanalysis]?.GetValue<bool>() == true)
            return new(AgentLoopActions.Reanalyze, "REANALYSIS_REQUIRED", "新證據可能改變結論，進入重分析。", iteration, false, evidenceCount, unresolved);

        var route = board[AgentBlackboardKeys.RouteDecision]?.GetValue<string>();
        if (route is not ("InsufficientEvidence" or "PartiallySupportedNeedsRetrieval"))
            return new(AgentLoopActions.FinalizeLimited, "EVIDENCE_VALIDATED", "證據已驗證，生成最終修訂。", iteration, false, evidenceCount, unresolved);

        var baseline = runtime["evidenceBaseline"]?.GetValue<int>() ?? 0;
        var unresolvedBaseline = runtime["unresolvedClaimsBaseline"]?.GetValue<string>() ?? string.Empty;
        if (evidenceCount <= baseline && string.Equals(unresolvedFingerprint, unresolvedBaseline, StringComparison.Ordinal))
            return ContinueLimited(AgentLoopStopReasons.NoProgress, "本輪沒有新增不同證據，且未解決 claim 未改變。", iteration, evidenceCount, unresolved);
        if (iteration >= ResearchQualityReviewWorkflow.MaxRetrievalIterations)
            return ContinueLimited(AgentLoopStopReasons.MaxIterations, "檢索迭代預算已用盡。", iteration, evidenceCount, unresolved);
        if (dynamicNodeCount + 7 > ResearchQualityReviewWorkflow.MaxDynamicNodes)
            return BudgetLimited(dynamicNodeCount, iteration, evidenceCount, unresolved);
        return new(AgentLoopActions.RetrieveEvidence, "EVIDENCE_GAP", "仍有未解決的證據缺口。", iteration + 1, false, evidenceCount, unresolved);
    }

    private static AgentLoopDecision ContinueLimited(string code, string reason, int iteration, int evidenceCount, IReadOnlyList<string> unresolved) =>
        new(AgentLoopActions.FinalizeLimited, code, reason, iteration, false, evidenceCount, unresolved);
    private static AgentLoopDecision BudgetLimited(int dynamicNodeCount, int iteration, int evidenceCount, IReadOnlyList<string> unresolved) =>
        dynamicNodeCount + 3 <= ResearchQualityReviewWorkflow.MaxDynamicNodes
            ? ContinueLimited(AgentLoopStopReasons.DynamicNodeLimit, "動態節點預算不足以再檢索，將以有限證據完成。", iteration, evidenceCount, unresolved)
            : Stop(AgentLoopStopReasons.DynamicNodeLimit, "動態節點預算已用盡，保留目前可用輸出。", iteration, evidenceCount, unresolved);
    private static AgentLoopDecision Stop(string code, string reason, int iteration, int evidenceCount, IReadOnlyList<string> unresolved) =>
        new(AgentLoopActions.Complete, code, reason, iteration, true, evidenceCount, unresolved);
    private static bool IsRetrievalNode(AgentRunNode node) => node.NodeType is EvidenceRemediationNodeTypes.RetrieveEvidence or EvidenceRemediationNodeTypes.RetrieveWebEvidence;

    private static int DistinctEvidenceCount(JsonObject board) =>
        (board[AgentBlackboardKeys.RetrievedEvidence] as JsonArray)?.Select(x =>
            x?["documentChunkId"]?.ToJsonString() ?? x?["url"]?.GetValue<string>() ?? x?.ToJsonString())
            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).Count() ?? 0;

    private static IReadOnlyList<string> UnresolvedClaimIds(JsonObject board) =>
        (board[AgentBlackboardKeys.UnresolvedClaims] as JsonArray)?.Select((x, index) => x switch
        {
            JsonValue value when value.TryGetValue<string>(out var claimId) && !string.IsNullOrWhiteSpace(claimId) => claimId,
            JsonObject claim => claim["claimId"]?.GetValue<string>() ?? claim["id"]?.GetValue<string>() ?? $"claim:{index}",
            _ => $"claim:{index}"
        })
            .Distinct(StringComparer.Ordinal).ToList() ?? [];
}

public sealed class PortfolioDiagnosisLoopPolicy : IAgentLoopPolicy
{
    public string WorkflowType => AgentWorkflowTypes.PortfolioDiagnosis;

    public AgentLoopDecision Evaluate(AgentRun run)
    {
        var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        var runtime = board[AgentBlackboardKeys.Runtime] as JsonObject ?? new JsonObject();
        board[AgentBlackboardKeys.Runtime] = runtime;
        var last = run.Nodes.Where(x => x.Status == AgentNodeStatuses.Succeeded)
            .OrderByDescending(x => x.CompletedAtUtc).FirstOrDefault();
        var quality = board[AgentBlackboardKeys.PortfolioDiagnosisQuality]
            ?.Deserialize<PortfolioDiagnosisQualityResult>(AgentNodeJson.SerializerOptions);
        var iteration = quality?.Iteration ?? runtime["iteration"]?.GetValue<int>() ?? 0;
        var gaps = quality?.Gaps.Select(x => x.Code).Distinct(StringComparer.Ordinal).ToList() ?? [];
        var required = quality?.RequiredCapabilities.Distinct(StringComparer.Ordinal).ToList() ?? [];
        var completedCount = quality?.CompletedCapabilities.Count ?? 0;
        var dynamicNodeCount = run.Nodes.Count(x => !string.IsNullOrWhiteSpace(x.TemplateNodeKey));

        AgentLoopDecision decision;
        if (last?.NodeType == PortfolioDiagnosisNodeTypes.FinalizeDiagnosis)
        {
            decision = Stop(runtime["pendingStopReason"]?.GetValue<string>() ?? AgentLoopStopReasons.QualityGatePassed,
                "投組診斷已批准並發布。", iteration, gaps, required, completedCount);
        }
        else if (quality is null)
        {
            throw new InvalidOperationException("Portfolio diagnosis quality result is missing.");
        }
        else if (quality.Status == PortfolioDiagnosisQualityStatuses.NeedsAnalysis)
        {
            var baselineCount = runtime["completedCapabilityBaseline"]?.GetValue<int>() ?? -1;
            var baselineGaps = runtime["gapBaseline"]?.GetValue<string>() ?? string.Empty;
            var fingerprint = string.Join('|', gaps.Order(StringComparer.Ordinal));
            if (iteration > 0 && completedCount <= baselineCount && string.Equals(fingerprint, baselineGaps, StringComparison.Ordinal))
                decision = Finalize(AgentLoopStopReasons.NoProgress, "補算後沒有新增可用分析。", iteration, gaps, required, completedCount);
            else if (iteration >= PortfolioDiagnosisWorkflow.MaxAnalysisIterations)
                decision = Finalize(AgentLoopStopReasons.MaxIterations, "投組補算迭代預算已用盡。", iteration, gaps, required, completedCount);
            else if (required.Count > PortfolioDiagnosisWorkflow.MaxMathCapabilitiesPerIteration
                || dynamicNodeCount + required.Count + 1 > PortfolioDiagnosisWorkflow.MaxDynamicNodes)
                decision = Finalize(AgentLoopStopReasons.DynamicNodeLimit, "動態節點預算不足，將產生有限診斷。", iteration, gaps, required, completedCount);
            else
                decision = new(AgentLoopActions.RunRiskAnalyses, "ANALYSIS_GAPS", "品質閘門要求補做風險分析。",
                    iteration + 1, false, 0, [], gaps, required, completedCount);
        }
        else if (quality.Status == PortfolioDiagnosisQualityStatuses.Limited)
            decision = Finalize(AgentLoopStopReasons.DataLimited, "資料限制無法由數學補算解決，將產生有限診斷。", iteration, gaps, required, completedCount);
        else
            decision = Finalize(AgentLoopStopReasons.QualityGatePassed, "投組診斷品質閘門已通過。", iteration, gaps, required, completedCount);

        runtime["iteration"] = decision.Iteration;
        runtime["status"] = decision.IsTerminal ? "Stopped" : "Running";
        runtime["lastAction"] = decision.Action;
        runtime["lastReasonCode"] = decision.ReasonCode;
        runtime["qualityStatus"] = quality?.Status;
        runtime["gapCodes"] = JsonSerializer.SerializeToNode(gaps, AgentNodeJson.SerializerOptions);
        runtime["completedCapabilities"] = JsonSerializer.SerializeToNode(quality?.CompletedCapabilities ?? [], AgentNodeJson.SerializerOptions);
        runtime["stopReason"] = decision.IsTerminal ? decision.ReasonCode : null;
        if (decision.Action == AgentLoopActions.RunRiskAnalyses)
        {
            runtime["completedCapabilityBaseline"] = completedCount;
            runtime["gapBaseline"] = string.Join('|', gaps.Order(StringComparer.Ordinal));
        }
        if (decision.Action == AgentLoopActions.FinalizeDiagnosis)
        {
            runtime["pendingStopReason"] = decision.ReasonCode;
            runtime["stopReason"] = decision.ReasonCode;
        }
        run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);
        return decision;
    }

    private static AgentLoopDecision Finalize(string code, string reason, int iteration,
        IReadOnlyList<string> gaps, IReadOnlyList<string> required, int completedCount) =>
        new(AgentLoopActions.FinalizeDiagnosis, code, reason, iteration, false, 0, [], gaps, required, completedCount);

    private static AgentLoopDecision Stop(string code, string reason, int iteration,
        IReadOnlyList<string> gaps, IReadOnlyList<string> required, int completedCount) =>
        new(AgentLoopActions.Complete, code, reason, iteration, true, 0, [], gaps, required, completedCount);
}
