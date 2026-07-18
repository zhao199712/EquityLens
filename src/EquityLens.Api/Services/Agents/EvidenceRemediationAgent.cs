using System.Text.Json;
using EquityLens.Api.Services.Ai;

namespace EquityLens.Api.Services.Agents;

public static class EvidenceClaimKinds
{
    public const string AnswerClaim = "AnswerClaim";
    public const string InvestigationClaim = "InvestigationClaim";
}

public sealed record EvidenceClaim(string Id, string Text, IReadOnlyList<string> NumericValues, string Kind = EvidenceClaimKinds.AnswerClaim);
public sealed record RemediationEvidenceItem(int Index, string SourceType, string? Title, string? DocumentType, string? Url, string Content, double RelevanceScore, DateTimeOffset? PublishedAt = null, string? Query = null, string? Provider = null);
public sealed record ClaimSupportAssessment(
    string ClaimId,
    string Status,
    IReadOnlyList<int> EvidenceIndexes,
    string Reason,
    double Confidence = 0,
    string AnalysisImpact = "None",
    string ImpactReason = "");
public sealed record ValidatedClaimSupport(string ClaimId, string ClaimText, string Status, IReadOnlyList<int> EvidenceIndexes, IReadOnlyList<string> ValidationErrors);
public sealed record EvidenceValidationResult(string EvidenceStatus, IReadOnlyList<ValidatedClaimSupport> Claims, IReadOnlyList<string> UnresolvedClaimIds, bool RequiresReanalysis = false, IReadOnlyList<string>? ReanalysisReasons = null);
public sealed record RemediatedEvidencePacket(string EvidenceStatus, IReadOnlyList<ValidatedClaimSupport> Claims, IReadOnlyList<RemediationEvidenceItem> Evidence, IReadOnlyList<string> UnresolvedClaimIds, bool RequiresReanalysis = false, IReadOnlyList<string>? ReanalysisReasons = null);
public sealed record EvidenceBackedRevisionResult(string RevisedAnswer, string RevisionSummary);
public sealed record EvidenceRemediationOutput(string SourceAnswer, string RevisedAnswer, string RevisionSummary, string EvidenceStatus, IReadOnlyList<RemediationEvidenceItem> Citations, IReadOnlyList<string> UnresolvedClaimIds, bool RequiresReanalysis = false, IReadOnlyList<string>? ReanalysisReasons = null);

public interface IClaimExtractionAgent
{
    Task<IReadOnlyList<EvidenceClaim>> ExtractAsync(ClaimExtractionInput input, CancellationToken cancellationToken = default);
}

public sealed record ClaimExtractionInput(string Question, string Answer, IReadOnlyList<CriticFinding> CriticFindings);

public interface IEvidenceBackedRevisionAgent
{
    Task<EvidenceBackedRevisionResult> ReviseAsync(string question, string sourceAnswer, RemediatedEvidencePacket packet, CancellationToken cancellationToken = default);
}

public sealed class LlmEvidenceRemediationAgent : IClaimExtractionAgent, IEvidenceBackedRevisionAgent
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IChatCompletionService _chat;

    public LlmEvidenceRemediationAgent(IChatCompletionService chat) => _chat = chat;

    public async Task<IReadOnlyList<EvidenceClaim>> ExtractAsync(ClaimExtractionInput input, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input.Answer) && string.IsNullOrWhiteSpace(input.Question) && input.CriticFindings.Count == 0)
            throw new InvalidOperationException("Claim extraction requires an answer, question, or Critic finding.");
        var content = await CompleteAsync("Extract factual claims from the answer. If the answer abstains or says evidence is insufficient, create investigation claims from the user question and Critic findings instead. Output JSON only: {\"claims\":[{\"id\":\"claim-1\",\"text\":\"...\",\"numericValues\":[\"123\"],\"kind\":\"AnswerClaim|InvestigationClaim\"}]}. Keep exact numbers, dates, percentages and currencies. Never return an empty claims array when a research question exists.", JsonSerializer.Serialize(input, JsonOptions), cancellationToken);
        var claims = JsonSerializer.Deserialize<ClaimEnvelope>(content, JsonOptions)?.Claims;
        var normalized = claims is { Count: > 0 } ? Normalize(claims) : [];
        if (IsAbstention(input.Answer) && normalized.All(x => x.Kind != EvidenceClaimKinds.InvestigationClaim)) return CreateInvestigationFallback(input);
        return normalized.Count > 0 ? normalized : CreateInvestigationFallback(input);
    }

    public async Task<EvidenceBackedRevisionResult> ReviseAsync(string question, string sourceAnswer, RemediatedEvidencePacket packet, CancellationToken cancellationToken = default)
    {
        var input = JsonSerializer.Serialize(new { question, sourceAnswer, packet }, JsonOptions);
        var content = await CompleteAsync("Revise the answer in Traditional Chinese using only validated evidence. Cite evidence as [n]. Output JSON only: {\"revisedAnswer\":\"...\",\"revisionSummary\":\"...\"}. Do not fabricate facts or citations.", input, cancellationToken);
        return JsonSerializer.Deserialize<EvidenceBackedRevisionResult>(content, JsonOptions) ?? throw new InvalidOperationException("Evidence-backed revision returned no result.");
    }

    private async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        var response = await _chat.CompleteAsync(new ChatCompletionRequest(systemPrompt, userPrompt, 0.1, 4096, ChatResponseFormat.JsonObject), cancellationToken);
        if (string.IsNullOrWhiteSpace(response.Content)) throw new InvalidOperationException("Evidence remediation LLM returned empty content.");
        return response.Content;
    }

    private sealed record ClaimEnvelope(IReadOnlyList<EvidenceClaim> Claims);

    private static IReadOnlyList<EvidenceClaim> Normalize(IReadOnlyList<EvidenceClaim> claims) => claims
        .Where(x => !string.IsNullOrWhiteSpace(x.Text))
        .Select((x, index) => x with { Id = string.IsNullOrWhiteSpace(x.Id) ? $"claim-{index + 1}" : x.Id, Kind = x.Kind is EvidenceClaimKinds.AnswerClaim or EvidenceClaimKinds.InvestigationClaim ? x.Kind : EvidenceClaimKinds.AnswerClaim })
        .ToList();

    internal static IReadOnlyList<EvidenceClaim> CreateInvestigationFallback(ClaimExtractionInput input)
    {
        var text = !string.IsNullOrWhiteSpace(input.Question)
            ? input.Question.Trim()
            : input.CriticFindings.Select(x => x.Message).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Claim extraction returned no claims and no investigation target is available.");
        return [new EvidenceClaim("investigation-claim-1", text, DeterministicEvidenceRemediationAgent.ExtractNumbers(text), EvidenceClaimKinds.InvestigationClaim)];
    }

    internal static bool IsAbstention(string value) => new[] { "資料不足", "證據不足", "無法回答", "無法判斷", "insufficient evidence", "cannot answer" }
        .Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
}

public sealed class DeterministicEvidenceRemediationAgent : IClaimExtractionAgent, IEvidenceBackedRevisionAgent
{
    public Task<IReadOnlyList<EvidenceClaim>> ExtractAsync(ClaimExtractionInput input, CancellationToken cancellationToken = default) =>
        Task.FromResult(!string.IsNullOrWhiteSpace(input.Answer) && !LlmEvidenceRemediationAgent.IsAbstention(input.Answer)
            ? (IReadOnlyList<EvidenceClaim>)[new("claim-1", input.Answer, ExtractNumbers(input.Answer))]
            : LlmEvidenceRemediationAgent.CreateInvestigationFallback(input));

    public Task<EvidenceBackedRevisionResult> ReviseAsync(string question, string sourceAnswer, RemediatedEvidencePacket packet, CancellationToken cancellationToken = default) =>
        Task.FromResult(new EvidenceBackedRevisionResult($"{sourceAnswer}\n\n補充證據：[1] {packet.Evidence[0].Content}", "已依驗證證據補強回答。"));

    internal static IReadOnlyList<string> ExtractNumbers(string value) =>
        System.Text.RegularExpressions.Regex.Matches(value, @"-?\d+(?:\.\d+)?%?").Select(match => match.Value).Distinct().ToList();
}
