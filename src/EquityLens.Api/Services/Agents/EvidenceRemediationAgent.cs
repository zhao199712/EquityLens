using System.Text.Json;
using EquityLens.Api.Services.Ai;

namespace EquityLens.Api.Services.Agents;

public sealed record EvidenceClaim(string Id, string Text, IReadOnlyList<string> NumericValues);
public sealed record RemediationEvidenceItem(int Index, string SourceType, string? Title, string? DocumentType, string? Url, string Content, double RelevanceScore);
public sealed record ClaimSupportAssessment(string ClaimId, string Status, IReadOnlyList<int> EvidenceIndexes, string Reason);
public sealed record ValidatedClaimSupport(string ClaimId, string ClaimText, string Status, IReadOnlyList<int> EvidenceIndexes, IReadOnlyList<string> ValidationErrors);
public sealed record EvidenceValidationResult(string EvidenceStatus, IReadOnlyList<ValidatedClaimSupport> Claims, IReadOnlyList<string> UnresolvedClaimIds);
public sealed record RemediatedEvidencePacket(string EvidenceStatus, IReadOnlyList<ValidatedClaimSupport> Claims, IReadOnlyList<RemediationEvidenceItem> Evidence, IReadOnlyList<string> UnresolvedClaimIds);
public sealed record EvidenceBackedRevisionResult(string RevisedAnswer, string RevisionSummary);
public sealed record EvidenceRemediationOutput(string SourceAnswer, string RevisedAnswer, string RevisionSummary, string EvidenceStatus, IReadOnlyList<RemediationEvidenceItem> Citations, IReadOnlyList<string> UnresolvedClaimIds);

public interface IClaimExtractionAgent
{
    Task<IReadOnlyList<EvidenceClaim>> ExtractAsync(string answer, CancellationToken cancellationToken = default);
}

public interface IClaimSupportAgent
{
    Task<IReadOnlyList<ClaimSupportAssessment>> AssessAsync(IReadOnlyList<EvidenceClaim> claims, IReadOnlyList<RemediationEvidenceItem> evidence, CancellationToken cancellationToken = default);
}

public interface IEvidenceBackedRevisionAgent
{
    Task<EvidenceBackedRevisionResult> ReviseAsync(string question, string sourceAnswer, RemediatedEvidencePacket packet, CancellationToken cancellationToken = default);
}

public sealed class LlmEvidenceRemediationAgent : IClaimExtractionAgent, IClaimSupportAgent, IEvidenceBackedRevisionAgent
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IChatCompletionService _chat;

    public LlmEvidenceRemediationAgent(IChatCompletionService chat) => _chat = chat;

    public async Task<IReadOnlyList<EvidenceClaim>> ExtractAsync(string answer, CancellationToken cancellationToken = default)
    {
        var content = await CompleteAsync("Extract factual claims from the answer. Output JSON only: {\"claims\":[{\"id\":\"claim-1\",\"text\":\"...\",\"numericValues\":[\"123\"]}]}. Keep exact numbers, dates, percentages and currencies.", answer, cancellationToken);
        return JsonSerializer.Deserialize<ClaimEnvelope>(content, JsonOptions)?.Claims ?? throw new InvalidOperationException("Claim extraction returned no claims.");
    }

    public async Task<IReadOnlyList<ClaimSupportAssessment>> AssessAsync(IReadOnlyList<EvidenceClaim> claims, IReadOnlyList<RemediationEvidenceItem> evidence, CancellationToken cancellationToken = default)
    {
        var input = JsonSerializer.Serialize(new { claims, evidence = evidence.Select(x => new { x.Index, x.Title, x.Content }) }, JsonOptions);
        var content = await CompleteAsync("Assess whether evidence supports each claim. Output JSON only: {\"assessments\":[{\"claimId\":\"claim-1\",\"status\":\"Supported|PartiallySupported|Unsupported|Contradicted|Unverifiable\",\"evidenceIndexes\":[1],\"reason\":\"...\"}]}. Never invent an evidence index.", input, cancellationToken);
        return JsonSerializer.Deserialize<AssessmentEnvelope>(content, JsonOptions)?.Assessments ?? throw new InvalidOperationException("Claim support assessment returned no results.");
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
    private sealed record AssessmentEnvelope(IReadOnlyList<ClaimSupportAssessment> Assessments);
}

public sealed class DeterministicEvidenceRemediationAgent : IClaimExtractionAgent, IClaimSupportAgent, IEvidenceBackedRevisionAgent
{
    public Task<IReadOnlyList<EvidenceClaim>> ExtractAsync(string answer, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EvidenceClaim>>([new("claim-1", answer, ExtractNumbers(answer))]);

    public Task<IReadOnlyList<ClaimSupportAssessment>> AssessAsync(IReadOnlyList<EvidenceClaim> claims, IReadOnlyList<RemediationEvidenceItem> evidence, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ClaimSupportAssessment>>(claims.Select(claim => new ClaimSupportAssessment(claim.Id, evidence.Count > 0 ? "Supported" : "Unsupported", evidence.Count > 0 ? [1] : [], evidence.Count > 0 ? "Evidence available." : "No evidence available.")).ToList());

    public Task<EvidenceBackedRevisionResult> ReviseAsync(string question, string sourceAnswer, RemediatedEvidencePacket packet, CancellationToken cancellationToken = default) =>
        Task.FromResult(new EvidenceBackedRevisionResult($"{sourceAnswer}\n\n補充證據：[1] {packet.Evidence[0].Content}", "已依驗證證據補強回答。"));

    internal static IReadOnlyList<string> ExtractNumbers(string value) =>
        System.Text.RegularExpressions.Regex.Matches(value, @"-?\d+(?:\.\d+)?%?").Select(match => match.Value).Distinct().ToList();
}
