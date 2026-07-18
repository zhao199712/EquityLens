using System.Text.Json;
using EquityLens.Api.Services.Ai;

namespace EquityLens.Api.Services.Agents;

public sealed record EvidenceAssessmentInput(
    string Question,
    string SourceAnswer,
    IReadOnlyList<EvidenceClaim> Claims,
    IReadOnlyList<RemediationEvidenceItem> Evidence,
    IReadOnlyList<CriticFinding> CriticFindings);

public sealed record EvidenceAssessorResult(
    IReadOnlyList<ClaimSupportAssessment> Assessments,
    string AgentIdentity,
    string Provider,
    string Model,
    int PromptTokens,
    int CompletionTokens,
    decimal? EstimatedCostUsd);

public interface IEvidenceAssessor
{
    Task<EvidenceAssessorResult> AssessAsync(EvidenceAssessmentInput input, CancellationToken cancellationToken = default);
}

public sealed class LlmEvidenceAssessor(IChatCompletionService chat) : IEvidenceAssessor
{
    public const string AgentIdentity = "EvidenceAssessor";
    public const string PromptTemplateId = "evidence-remediation-assessor";
    public const int PromptVersion = 2;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<EvidenceAssessorResult> AssessAsync(EvidenceAssessmentInput input, CancellationToken cancellationToken = default)
    {
        var userPrompt = JsonSerializer.Serialize(new
        {
            input.Question,
            input.SourceAnswer,
            input.Claims,
            evidence = input.Evidence.Select(x => new { x.Index, x.SourceType, x.Title, x.DocumentType, x.Url, x.Content }),
            input.CriticFindings
        }, JsonOptions);
        var response = await chat.CompleteAsync(new ChatCompletionRequest(SystemPrompt, userPrompt, 0.1, 4096, ChatResponseFormat.JsonObject), cancellationToken);
        if (string.IsNullOrWhiteSpace(response.Content)) throw new InvalidOperationException("Evidence assessor returned empty content.");
        var assessments = JsonSerializer.Deserialize<AssessmentEnvelope>(response.Content, JsonOptions)?.Assessments;
        if (assessments is null || assessments.Count == 0) throw new InvalidOperationException("Evidence assessor returned no assessments.");
        return new EvidenceAssessorResult(assessments, AgentIdentity, chat.Provider, response.Model, response.PromptTokens, response.CompletionTokens, null);
    }

    private const string SystemPrompt = """
You are the Evidence Assessor for Taiwan public-equity research. Determine only whether the supplied evidence supports each domain claim and whether validated new evidence could materially change the existing analysis. Do not search, invent facts, write an investment conclusion, or alter workflow routing.

Return JSON only: {"assessments":[{"claimId":"claim-1","status":"Supported|PartiallySupported|Unsupported|Contradicted|Unverifiable","evidenceIndexes":[1],"confidence":0.0,"reason":"...","analysisImpact":"None|WordingOnly|Material","impactReason":"..."}]}.

For Factual claims require direct documentary support. For Mechanism claims distinguish cited facts from bounded financial reasoning. For Judgment claims require enough mapped evidence for a conditional conclusion and use PartiallySupported when important dimensions remain uncertain. Answerability claims describe the research process and cannot establish that the user's question has been answered. Primary sources are preferred for guidance, reported numbers, and company policy. Secondary web sources may support directional analysis with lower confidence, but never an invented precise forecast. Use Material only when the cited evidence contradicts or materially changes a key number, reporting period, outlook, valuation assumption, risk, or investment conclusion in the source answer. Citation completion, wording corrections, and non-core details are WordingOnly or None. Never cite an evidence index that was not supplied. Assess every claim exactly once.
""";

    private sealed record AssessmentEnvelope(IReadOnlyList<ClaimSupportAssessment> Assessments);
}
