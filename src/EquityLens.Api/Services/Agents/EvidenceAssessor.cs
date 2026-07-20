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
    decimal? EstimatedCostUsd,
    string Mode = "Llm",
    string? FallbackReason = null,
    IReadOnlyList<StructuredLlmAttempt>? Attempts = null);

public interface IEvidenceAssessor
{
    Task<EvidenceAssessorResult> AssessAsync(EvidenceAssessmentInput input, CancellationToken cancellationToken = default);
}

public sealed class LlmEvidenceAssessor(IChatCompletionService chat) : IEvidenceAssessor
{
    public const string AgentIdentity = "EvidenceAssessor";
    public const string PromptTemplateId = "evidence-remediation-assessor";
    public const int PromptVersion = 4;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<EvidenceAssessorResult> AssessAsync(EvidenceAssessmentInput input, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            input.Question,
            input.SourceAnswer,
            input.Claims,
            evidence = input.Evidence.Select(x => new { x.Index, x.SourceType, x.Title, x.DocumentType, x.Url, x.Content }),
            input.CriticFindings
        };
        var execution = await StructuredLlmOutput.ExecuteAsync(chat, SystemPrompt, (attempt, previous, error) => JsonSerializer.Serialize(new
        {
            task = attempt == 1 ? "Assess every supplied claim." : "Repair the prior response and return one complete replacement JSON object.",
            input = payload,
            allowedClaimIds = input.Claims.Select(x => x.Id),
            allowedEvidenceIndexes = input.Evidence.Select(x => x.Index),
            previousOutput = attempt == 1 ? null : previous,
            validationError = attempt == 1 ? null : error
        }, JsonOptions), content => ParseAndValidate(content, input), 4096, cancellationToken);
        var promptTokens = execution.Attempts.Sum(x => x.PromptTokens); var completionTokens = execution.Attempts.Sum(x => x.CompletionTokens);
        if (execution.Value is not null) return new(execution.Value, AgentIdentity, chat.Provider, execution.Attempts[^1].Model, promptTokens, completionTokens, null, execution.Attempts.Count == 1 ? "Llm" : "LlmRepair", null, execution.Attempts);
        var fallback = input.Claims.Select(x => new ClaimSupportAssessment(x.Id, "Unverifiable", [], "Assessor structured output could not be validated.", 0, "None", "", "None", "NoChange")).ToList();
        return new(fallback, AgentIdentity, chat.Provider, execution.Attempts.LastOrDefault()?.Model ?? chat.Model, promptTokens, completionTokens, null, "ConservativeFallback", execution.LastError, execution.Attempts);
    }

    private static IReadOnlyList<ClaimSupportAssessment> ParseAndValidate(string content, EvidenceAssessmentInput input)
    {
        using var document = JsonDocument.Parse(content);
        if (!document.RootElement.TryGetProperty("assessments", out var rawAssessments) || rawAssessments.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("Evidence assessor response is missing assessments.");
        var requiredFields = new[] { "claimId", "status", "evidenceIndexes", "reason", "confidence", "analysisImpact", "impactReason", "questionRelevance", "answerabilityEffect" };
        if (rawAssessments.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.Object || requiredFields.Any(field => !item.TryGetProperty(field, out _)))) throw new InvalidOperationException("Evidence assessor response is missing required assessment fields.");
        var assessments = JsonSerializer.Deserialize<AssessmentEnvelope>(content, JsonOptions)?.Assessments ?? throw new InvalidOperationException("Evidence assessor returned no assessments.");
        if (assessments.Count == 0) throw new InvalidOperationException("Evidence assessor returned no assessments.");
        var allowedClaims = input.Claims.Select(x => x.Id).ToHashSet(StringComparer.Ordinal); var allowedEvidence = input.Evidence.Select(x => x.Index).ToHashSet();
        if (assessments.Select(x => x.ClaimId).Distinct(StringComparer.Ordinal).Count() != assessments.Count) throw new InvalidOperationException("Evidence assessor returned a duplicate claim id.");
        if (assessments.Any(x => !allowedClaims.Contains(x.ClaimId))) throw new InvalidOperationException("Evidence assessor returned an unknown claim id.");
        var missing = allowedClaims.Except(assessments.Select(x => x.ClaimId), StringComparer.Ordinal).FirstOrDefault(); if (missing is not null) throw new InvalidOperationException($"Evidence assessor omitted claim '{missing}'.");
        if (assessments.Any(x => x.Status is not ("Supported" or "PartiallySupported" or "Unsupported" or "Contradicted" or "Unverifiable"))) throw new InvalidOperationException("Evidence assessor returned an invalid status.");
        if (assessments.Any(x => x.AnalysisImpact is not ("None" or "WordingOnly" or "Material"))) throw new InvalidOperationException("Evidence assessor returned an invalid analysisImpact.");
        if (assessments.Any(x => x.QuestionRelevance is not ("None" or "Peripheral" or "Core"))) throw new InvalidOperationException("Evidence assessor returned an invalid questionRelevance.");
        if (assessments.Any(x => x.AnswerabilityEffect is not ("NoChange" or "EnablesBoundedAnswer" or "EnablesDirectAnswer"))) throw new InvalidOperationException("Evidence assessor returned an invalid answerabilityEffect.");
        if (assessments.Any(x => x.Confidence is < 0 or > 1)) throw new InvalidOperationException("Evidence assessor returned confidence outside 0-1.");
        if (assessments.SelectMany(x => x.EvidenceIndexes).Any(x => !allowedEvidence.Contains(x))) throw new InvalidOperationException("Evidence assessor returned an invalid evidence index.");
        return assessments;
    }

    private const string SystemPrompt = """
You are the Evidence Assessor for Taiwan public-equity research. Determine only whether the supplied evidence supports each domain claim and whether validated new evidence could materially change the existing analysis. Do not search, invent facts, write an investment conclusion, or alter workflow routing.

Return JSON only: {"assessments":[{"claimId":"claim-1","status":"Supported|PartiallySupported|Unsupported|Contradicted|Unverifiable","evidenceIndexes":[1],"confidence":0.0,"reason":"...","analysisImpact":"None|WordingOnly|Material","impactReason":"...","questionRelevance":"None|Peripheral|Core","answerabilityEffect":"NoChange|EnablesBoundedAnswer|EnablesDirectAnswer"}]}.

For Factual claims require direct documentary support. For Mechanism claims distinguish cited facts from bounded financial reasoning. For Judgment claims require enough mapped evidence for a conditional conclusion and use PartiallySupported when important dimensions remain uncertain. Answerability claims describe the research process and cannot establish that the user's question has been answered. Primary sources are preferred for guidance, reported numbers, and company policy. Secondary web sources may support directional analysis with lower confidence, but never an invented precise forecast. Judge Material against the user's original question, research objective, and source answer together. Material includes filling a previously missing core dimension, converting an abstention into a bounded/direct answer, or changing a key number, period, outlook, valuation input, risk, or conclusion. Citation completion, wording corrections, and non-core details are WordingOnly or None. Preserve the supplied claim type; report any classification concern in reason instead of rewriting it. Use only supplied claim IDs and evidence indexes. Include every required field. Assess every claim exactly once; never omit or duplicate a claim.
""";

    private sealed record AssessmentEnvelope(IReadOnlyList<ClaimSupportAssessment> Assessments);
}
