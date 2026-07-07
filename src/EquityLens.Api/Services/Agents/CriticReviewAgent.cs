namespace EquityLens.Api.Services.Agents;

public interface ICriticReviewAgent
{
    Task<CriticReviewResult> CritiqueAsync(CriticReviewInput input, CancellationToken cancellationToken = default);
}

public sealed record CriticReviewInput(
    string? Ticker,
    string? Question,
    string? Answer,
    int CitationCount,
    int CandidateCount,
    string SourceStatus,
    IReadOnlyList<CriticFinding> EvidenceFindings);

public sealed record CriticReviewResult(
    string Summary,
    string OverallSeverity,
    IReadOnlyList<CriticFinding> Findings,
    string? SuggestedAnswerRevision);

public sealed record CriticFinding(
    string Severity,
    string Category,
    string Message,
    IReadOnlyList<int> RelatedCitationIndexes,
    string Recommendation);

public sealed class DeterministicCriticReviewAgent : ICriticReviewAgent
{
    public Task<CriticReviewResult> CritiqueAsync(CriticReviewInput input, CancellationToken cancellationToken = default)
    {
        var findings = input.EvidenceFindings.ToList();
        if (string.IsNullOrWhiteSpace(input.Answer))
        {
            findings.Add(new CriticFinding(
                "Critical",
                "MissingAnswer",
                "回答內容為空，無法進行品質審查。",
                [],
                "先產生可審查的回答內容。"));
        }

        var overallSeverity = DetermineOverallSeverity(findings);
        var summary = findings.Count == 0
            ? "未發現明顯 citation 或證據覆蓋問題。"
            : $"發現 {findings.Count} 個回答品質或證據覆蓋問題。";

        return Task.FromResult(new CriticReviewResult(
            summary,
            overallSeverity,
            findings,
            findings.Count == 0 ? null : "建議補強引用支撐後再重寫回答，並避免超出來源證據的推論。"));
    }

    private static string DetermineOverallSeverity(IEnumerable<CriticFinding> findings)
    {
        var severities = findings
            .Select(x => x.Severity)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (severities.Contains("Critical")) return "Critical";
        if (severities.Contains("High")) return "High";
        if (severities.Contains("Medium")) return "Medium";
        if (severities.Contains("Low")) return "Low";
        return "None";
    }
}
