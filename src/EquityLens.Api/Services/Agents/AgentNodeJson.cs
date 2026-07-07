using System.Text.Json;
using System.Text.Json.Nodes;

namespace EquityLens.Api.Services.Agents;

internal static class AgentNodeJson
{
    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, SerializerOptions);

    public static JsonObject ParseBlackboard(string json) =>
        JsonNode.Parse(json)?.AsObject() ?? new JsonObject();

    public static JsonObject? GetBlackboardObject(JsonObject blackboard, string key) =>
        blackboard[key]?.AsObject();

    public static JsonObject GetRequiredBlackboardObject(JsonObject blackboard, string key) =>
        GetBlackboardObject(blackboard, key) ?? throw new InvalidOperationException($"Blackboard is missing {key}.");

    public static T? GetBlackboardValue<T>(JsonObject blackboard, string key) =>
        blackboard[key] is null ? default : blackboard[key]!.GetValue<T>();

    public static CriticReviewInput CreateCriticReviewInput(JsonObject blackboard)
    {
        var evidencePacket = GetRequiredBlackboardObject(blackboard, AgentBlackboardKeys.EvidencePacket);
        var evidenceChecks = GetBlackboardObject(blackboard, AgentBlackboardKeys.EvidenceChecks) ?? new JsonObject();
        var findings = (evidenceChecks[EvidenceCheckFields.Findings]?.AsArray() ?? [])
            .Select(ParseFinding)
            .Where(x => x is not null)
            .Cast<CriticFinding>()
            .ToList();

        return new CriticReviewInput(
            evidencePacket[AgentBlackboardKeys.Ticker]?.GetValue<string>(),
            evidencePacket[AgentBlackboardKeys.Question]?.GetValue<string>(),
            evidencePacket[AgentBlackboardKeys.Answer]?.GetValue<string>(),
            evidenceChecks[EvidenceCheckFields.CitationCount]?.GetValue<int>() ?? 0,
            evidenceChecks[EvidenceCheckFields.CandidateCount]?.GetValue<int>() ?? 0,
            evidenceChecks[EvidenceCheckFields.SourceStatus]?.GetValue<string>() ?? string.Empty,
            findings);
    }

    public static FinalizeCriticReportNodeOutput CreateFinalizeCriticReportNodeOutput(
        JsonObject criticReview,
        WorkflowPolicyDecision policyDecision)
    {
        var findings = (criticReview[CriticReviewFields.Findings]?.AsArray() ?? [])
            .Select(ParseFinding)
            .Where(x => x is not null)
            .Cast<CriticFinding>()
            .ToList();

        return new FinalizeCriticReportNodeOutput(
            criticReview[CriticReviewFields.Summary]?.GetValue<string>() ?? string.Empty,
            criticReview[CriticReviewFields.OverallSeverity]?.GetValue<string>() ?? "None",
            findings,
            policyDecision.RequiresRevision,
            policyDecision.RequiresMoreEvidence,
            policyDecision.RouteBackTo,
            policyDecision.RecommendedNextAction,
            criticReview[CriticReviewFields.SuggestedAnswerRevision]?.GetValue<string>());
    }

    public static CriticFinding? ParseFinding(JsonNode? node)
    {
        var finding = node?.AsObject();
        if (finding is null) return null;

        var relatedCitationIndexes = (finding[CriticFindingFields.RelatedCitationIndexes]?.AsArray() ?? [])
            .Select(x => x?.GetValue<int>())
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToList();

        return new CriticFinding(
            finding[CriticFindingFields.Severity]?.GetValue<string>() ?? "Low",
            finding[CriticFindingFields.Category]?.GetValue<string>() ?? "General",
            finding[CriticFindingFields.Message]?.GetValue<string>() ?? string.Empty,
            relatedCitationIndexes,
            finding[CriticFindingFields.Recommendation]?.GetValue<string>() ?? string.Empty);
    }

    public static string Trim(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";
}
