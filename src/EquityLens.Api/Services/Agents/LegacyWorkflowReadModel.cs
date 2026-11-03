using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Agents;
using EquityLens.Api.Data.Entities;
namespace EquityLens.Api.Services.Agents;

/// <summary>唯讀相容層：保留歷史 JSON 與 API 摘要，絕不建立或執行舊圖。</summary>
internal static class LegacyWorkflowReadModel
{
    public const string RetiredResearchEngine = "DynamicDag";
    public const string Research = "ResearchInvestigation";
    public static bool OwnsResearchArtifact(string type) => type is "ResearchInvestigation" or "FeedbackRevision";
    public static AgentLoopSummaryResponse? BuildLoopSummary(AgentRun run, IReadOnlyList<AgentRunNode> nodes)
    {
        if (nodes.Any(n => n.NodeType is "PortfolioAgentLoop" or "ResearchAgentLoop")) return null;
        if (run.WorkflowType is not ("ResearchQualityReview" or AgentWorkflowTypes.PortfolioDiagnosis)) return null;
        var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        var runtime = board[AgentBlackboardKeys.Runtime] as JsonObject;
        if (run.WorkflowType == AgentWorkflowTypes.PortfolioDiagnosis)
        {
            var quality = board[AgentBlackboardKeys.PortfolioDiagnosisQuality]
                ?.Deserialize<PortfolioDiagnosisQualityResult>(AgentNodeJson.SerializerOptions);
            var gaps = quality?.Gaps.Select(x => x.Code).Distinct(StringComparer.Ordinal).ToList()
                ?? (runtime?["gapCodes"] as JsonArray)?.Select(x => x?.GetValue<string>() ?? string.Empty)
                    .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToList()
                ?? [];
            var completed = quality?.CompletedCapabilities.Distinct(StringComparer.Ordinal).ToList()
                ?? (runtime?["completedCapabilities"] as JsonArray)?.Select(x => x?.GetValue<string>() ?? string.Empty)
                    .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToList()
                ?? [];
            var riskEvidence = board[AgentBlackboardKeys.PortfolioRiskEvidence]
                ?.Deserialize<PortfolioRiskEvidenceSnapshot>(AgentNodeJson.SerializerOptions);
            return new(
                LoopStatus(run, runtime),
                runtime?["iteration"]?.GetValue<int>() ?? quality?.Iteration ?? 0,
                runtime?["maxIterations"]?.GetValue<int>() ?? 1,
                runtime?["lastAction"]?.GetValue<string>(),
                runtime?["stopReason"]?.GetValue<string>(),
                nodes.Count(x => !string.IsNullOrWhiteSpace(x.TemplateNodeKey)),
                runtime?["maxDynamicNodes"]?.GetValue<int>() ?? 10,
                0,
                0,
                completed.Count,
                gaps,
                AgentWorkflowTypes.PortfolioDiagnosis,
                quality?.Status ?? runtime?["qualityStatus"]?.GetValue<string>(),
                gaps,
                completed,
                riskEvidence?.Source,
                riskEvidence?.CacheStatus,
                riskEvidence?.ReusedCapabilities,
                riskEvidence?.CalculatedCapabilities,
                riskEvidence?.SourceRiskRunIds,
                riskEvidence?.RejectedRuns.Select(x => x.Code).Distinct(StringComparer.Ordinal).ToList(),
                riskEvidence?.QualityStatus,
                riskEvidence?.QualityEvaluationId,
                riskEvidence?.QualityBacktestRunId,
                riskEvidence?.QualityWarnings);
        }
        var unresolved = (board[AgentBlackboardKeys.UnresolvedClaims] as JsonArray)?.Select((x, index) => x switch
        {
            JsonValue value when value.TryGetValue<string>(out var claimId) && !string.IsNullOrWhiteSpace(claimId) => claimId,
            JsonObject claim => claim["claimId"]?.GetValue<string>() ?? claim["id"]?.GetValue<string>() ?? $"claim:{index}",
            _ => $"claim:{index}"
        })
            .Distinct(StringComparer.Ordinal).ToList() ?? [];
        var evidenceCount = (board[AgentBlackboardKeys.RetrievedEvidence] as JsonArray)?.Select(x =>
            x?["documentChunkId"]?.ToJsonString() ?? x?["url"]?.GetValue<string>() ?? x?.ToJsonString())
            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).Count() ?? 0;
        return new(
            LoopStatus(run, runtime),
            runtime?["iteration"]?.GetValue<int>() ?? 0,
            runtime?["maxIterations"]?.GetValue<int>() ?? 2,
            runtime?["lastAction"]?.GetValue<string>(),
            runtime?["stopReason"]?.GetValue<string>(),
            nodes.Count(x => !string.IsNullOrWhiteSpace(x.TemplateNodeKey)),
            runtime?["maxDynamicNodes"]?.GetValue<int>() ?? 18,
            nodes.Count(x => x.NodeType == "RetrieveWebEvidence" && x.Status == AgentNodeStatuses.Succeeded),
            runtime?["maxWebRetrievals"]?.GetValue<int>() ?? 1,
            evidenceCount,
            unresolved,
            "ResearchQualityReview");
    }

    private static string LoopStatus(AgentRun run, JsonObject? runtime) => run.Status switch
    {
        AgentRunStatuses.Failed => "Failed",
        AgentRunStatuses.Cancelled => "Cancelled",
        _ => runtime?["status"]?.GetValue<string>() ?? "NotStarted"
    };

}
