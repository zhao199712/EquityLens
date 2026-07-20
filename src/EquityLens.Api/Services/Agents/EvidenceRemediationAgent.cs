using System.Text.Json;
using EquityLens.Api.Services.Ai;

namespace EquityLens.Api.Services.Agents;

public static class EvidenceClaimKinds
{
    public const string AnswerClaim = "AnswerClaim";
    public const string InvestigationClaim = "InvestigationClaim";
}

public static class EvidenceClaimTypes
{
    public const string Factual = "Factual";
    public const string Mechanism = "Mechanism";
    public const string Judgment = "Judgment";
    public const string Answerability = "Answerability";
}

public static class InvestigationModes
{
    public const string CorrectExistingAnswer = "CorrectExistingAnswer";
    public const string RecoverAnswer = "RecoverAnswer";
}

public sealed record EvidenceClaim(string Id, string Text, IReadOnlyList<string> NumericValues, string Kind = EvidenceClaimKinds.AnswerClaim, string ClaimType = EvidenceClaimTypes.Factual, string? ResearchDimension = null);
public sealed record RemediationEvidenceItem(int Index, string SourceType, string? Title, string? DocumentType, string? Url, string Content, double RelevanceScore, DateTimeOffset? PublishedAt = null, string? Query = null, string? Provider = null);
public sealed record ClaimSupportAssessment(
    string ClaimId,
    string Status,
    IReadOnlyList<int> EvidenceIndexes,
    string Reason,
    double Confidence = 0,
    string AnalysisImpact = "None",
    string ImpactReason = "",
    string QuestionRelevance = "Core",
    string AnswerabilityEffect = "NoChange");
public sealed record ValidatedClaimSupport(string ClaimId, string ClaimText, string Status, IReadOnlyList<int> EvidenceIndexes, IReadOnlyList<string> ValidationErrors);
public sealed record EvidenceValidationResult(string EvidenceStatus, IReadOnlyList<ValidatedClaimSupport> Claims, IReadOnlyList<string> UnresolvedClaimIds, bool RequiresReanalysis = false, IReadOnlyList<string>? ReanalysisReasons = null);
public sealed record RemediatedEvidencePacket(string EvidenceStatus, IReadOnlyList<ValidatedClaimSupport> Claims, IReadOnlyList<RemediationEvidenceItem> Evidence, IReadOnlyList<string> UnresolvedClaimIds, bool RequiresReanalysis = false, IReadOnlyList<string>? ReanalysisReasons = null);
public sealed record EvidenceBackedRevisionResult(string RevisedAnswer, string RevisionSummary, IReadOnlyList<string>? AnsweredDimensions = null, IReadOnlyList<string>? InferenceLimitations = null);
public sealed record AnswerQualityValidationResult(string Status, double Coverage, IReadOnlyList<string> AnsweredDimensions, IReadOnlyList<string> MissingDimensions, IReadOnlyList<string> Errors);
public sealed record EvidenceRemediationOutput(string SourceAnswer, string RevisedAnswer, string RevisionSummary, string EvidenceStatus, IReadOnlyList<RemediationEvidenceItem> Citations, IReadOnlyList<string> UnresolvedClaimIds, bool RequiresReanalysis = false, IReadOnlyList<string>? ReanalysisReasons = null, string InvestigationMode = InvestigationModes.CorrectExistingAnswer, double AnswerCoverage = 0, IReadOnlyList<string>? AnsweredDimensions = null, IReadOnlyList<string>? UnresolvedDimensions = null, IReadOnlyList<string>? InferenceLimitations = null, string AnswerQualityStatus = "NotEvaluated");

public interface IClaimExtractionAgent
{
    Task<IReadOnlyList<EvidenceClaim>> ExtractAsync(ClaimExtractionInput input, CancellationToken cancellationToken = default);
}

public sealed record ClaimExtractionInput(string Question, string Answer, IReadOnlyList<CriticFinding> CriticFindings, IReadOnlyList<string>? ValidationErrors = null, bool IsRepair = false);

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
        const string prompt = "Extract domain claims that help answer the user's research question. If the answer abstains, ignore its meta claim and decompose the question and Critic findings into answerable factual, mechanism, and judgment investigation claims. Cover every required research dimension identified by the question; claims must directly support retrieval or analysis, not merely say historical data may be useful. When validationErrors are supplied, repair every listed defect. Answerability statements such as 'data is insufficient' may be labelled Answerability but must not dominate. Output JSON only: {\"claims\":[{\"id\":\"claim-1\",\"text\":\"...\",\"numericValues\":[\"123\"],\"kind\":\"AnswerClaim|InvestigationClaim\",\"claimType\":\"Factual|Mechanism|Judgment|Answerability\",\"researchDimension\":\"...\"}]}. numericValues may contain JSON strings or numbers; never objects, arrays or booleans. Keep exact numbers, dates, percentages and currencies. Never return an empty claims array when a research question exists.";
        string? parseError = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var request = attempt == 0 ? input : input with { ValidationErrors = [parseError!], IsRepair = true };
            var content = await CompleteAsync(prompt, JsonSerializer.Serialize(request, JsonOptions), cancellationToken);
            try
            {
                var claims = ParseClaims(content); return claims.Count > 0 ? Normalize(claims) : [];
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                parseError = exception.Message;
            }
        }
        return CreateInvestigationFallback(input);
    }

    public async Task<EvidenceBackedRevisionResult> ReviseAsync(string question, string sourceAnswer, RemediatedEvidencePacket packet, CancellationToken cancellationToken = default)
    {
        var input = JsonSerializer.Serialize(new { question, sourceAnswer, packet }, JsonOptions);
        var mode = IsAbstention(sourceAnswer) ? InvestigationModes.RecoverAnswer : InvestigationModes.CorrectExistingAnswer;
        var content = await CompleteAsync($"Produce the final answer in Traditional Chinese using only validated evidence. Mode={mode}. In RecoverAnswer mode replace the abstention and directly answer the original question with: verified facts, bounded financial mechanisms, a conditional judgment, and explicit uncertainty. Distinguish inability to quantify precisely from inability to analyze. Secondary web sources may support directional analysis but not invented forecasts. Cite evidence as [n]. Output JSON only: {{\"revisedAnswer\":\"...\",\"revisionSummary\":\"...\",\"answeredDimensions\":[\"...\"],\"inferenceLimitations\":[\"...\"]}}. Do not fabricate facts or citations.", input, cancellationToken);
        return JsonSerializer.Deserialize<EvidenceBackedRevisionResult>(content, JsonOptions) ?? throw new InvalidOperationException("Evidence-backed revision returned no result.");
    }

    private async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        var response = await _chat.CompleteAsync(new ChatCompletionRequest(systemPrompt, userPrompt, 0.1, 4096, ChatResponseFormat.JsonObject), cancellationToken);
        if (string.IsNullOrWhiteSpace(response.Content)) throw new InvalidOperationException("Evidence remediation LLM returned empty content.");
        return response.Content;
    }

    private static IReadOnlyList<EvidenceClaim> ParseClaims(string content)
    {
        using var document = JsonDocument.Parse(content);
        if (!document.RootElement.TryGetProperty("claims", out var claimsElement) || claimsElement.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("Claim extraction response is missing claims array.");
        var claims = new List<EvidenceClaim>();
        foreach (var item in claimsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Claim extraction returned a non-object claim.");
            var text = RequiredString(item, "text");
            var numbers = new List<string>();
            if (item.TryGetProperty("numericValues", out var values))
            {
                if (values.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("Claim numericValues must be an array.");
                foreach (var value in values.EnumerateArray())
                {
                    var normalized = value.ValueKind switch
                    {
                        JsonValueKind.String => value.GetString(),
                        JsonValueKind.Number => value.GetRawText(),
                        _ => throw new InvalidOperationException("Claim numericValues may contain only strings or numbers.")
                    };
                    if (!string.IsNullOrWhiteSpace(normalized)) numbers.Add(normalized.Trim());
                }
            }
            claims.Add(new EvidenceClaim(OptionalString(item, "id") ?? string.Empty, text, numbers.Distinct(StringComparer.Ordinal).ToList(), OptionalString(item, "kind") ?? EvidenceClaimKinds.AnswerClaim, OptionalString(item, "claimType") ?? EvidenceClaimTypes.Factual, OptionalString(item, "researchDimension")));
        }
        return claims;
    }

    private static string RequiredString(JsonElement item, string name) => OptionalString(item, name) ?? throw new InvalidOperationException($"Claim extraction field '{name}' must be a non-empty string.");
    private static string? OptionalString(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        if (value.ValueKind != JsonValueKind.String) throw new InvalidOperationException($"Claim extraction field '{name}' must be a string.");
        var text = value.GetString()?.Trim(); return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static IReadOnlyList<EvidenceClaim> Normalize(IReadOnlyList<EvidenceClaim> claims) => claims
        .Where(x => !string.IsNullOrWhiteSpace(x.Text))
        .Select((x, index) => x with { Id = string.IsNullOrWhiteSpace(x.Id) ? $"claim-{index + 1}" : x.Id, Kind = x.Kind is EvidenceClaimKinds.AnswerClaim or EvidenceClaimKinds.InvestigationClaim ? x.Kind : EvidenceClaimKinds.AnswerClaim, ClaimType = x.ClaimType is EvidenceClaimTypes.Factual or EvidenceClaimTypes.Mechanism or EvidenceClaimTypes.Judgment or EvidenceClaimTypes.Answerability ? x.ClaimType : EvidenceClaimTypes.Factual })
        .ToList();

    internal static IReadOnlyList<EvidenceClaim> CreateInvestigationFallback(ClaimExtractionInput input)
    {
        var text = !string.IsNullOrWhiteSpace(input.Question)
            ? input.Question.Trim()
            : input.CriticFindings.Select(x => x.Message).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Claim extraction returned no claims and no investigation target is available.");
        if (ContainsAny(text, "資本支出", "capex", "自由現金流", "FCF", "股東回報", "股利"))
        {
            return
            [
                Investigation("capex-guidance", "未來資本支出的規模、期間與公司指引為何？", EvidenceClaimTypes.Factual, "CapEx guidance"),
                Investigation("fcf-impact", "資本支出增加在其他條件不變下會壓低短期自由現金流。", EvidenceClaimTypes.Mechanism, "FCF impact"),
                Investigation("cash-flow-coverage", "營業現金流與現金部位是否足以覆蓋資本支出。", EvidenceClaimTypes.Factual, "Cash flow coverage"),
                Investigation("depreciation-impact", "新增資本支出帶來的折舊可能如何影響獲利與現金流。", EvidenceClaimTypes.Mechanism, "Depreciation impact"),
                Investigation("shareholder-return", "自由現金流壓力是否會限制股利或其他股東回報的成長。", EvidenceClaimTypes.Judgment, "Shareholder returns"),
                Investigation("growth-offset", "新增產能帶來的營收與營業現金流成長是否可能抵銷資本支出壓力。", EvidenceClaimTypes.Judgment, "Growth offset")
            ];
        }
        return
        [
            Investigation("facts", $"回答「{text}」所需的關鍵可查證事實為何？", EvidenceClaimTypes.Factual, "Key facts"),
            Investigation("mechanism", $"哪些可驗證的因果機制會影響「{text}」？", EvidenceClaimTypes.Mechanism, "Mechanism"),
            Investigation("judgment", $"根據已驗證事實，對「{text}」可形成什麼有條件的判斷？", EvidenceClaimTypes.Judgment, "Conditional judgment")
        ];
    }

    private static EvidenceClaim Investigation(string id, string text, string type, string dimension) => new($"investigation-{id}", text, DeterministicEvidenceRemediationAgent.ExtractNumbers(text), EvidenceClaimKinds.InvestigationClaim, type, dimension);
    internal static bool IsMetaClaim(string value) => ContainsAny(value, "資料不足", "證據不足", "無法回答", "需要更多資料", "insufficient evidence", "cannot answer");
    private static bool ContainsAny(string value, params string[] markers) => markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    internal static bool IsAbstention(string value) => ContainsAny(value, "資料不足", "證據不足", "無法回答", "無法判斷", "insufficient evidence", "cannot answer");
}

public sealed class DeterministicEvidenceRemediationAgent : IClaimExtractionAgent, IEvidenceBackedRevisionAgent
{
    public Task<IReadOnlyList<EvidenceClaim>> ExtractAsync(ClaimExtractionInput input, CancellationToken cancellationToken = default) =>
        Task.FromResult(!string.IsNullOrWhiteSpace(input.Answer) && !LlmEvidenceRemediationAgent.IsAbstention(input.Answer)
            ? (IReadOnlyList<EvidenceClaim>)[new("claim-1", input.Answer, ExtractNumbers(input.Answer))]
            : LlmEvidenceRemediationAgent.CreateInvestigationFallback(input));

    public Task<EvidenceBackedRevisionResult> ReviseAsync(string question, string sourceAnswer, RemediatedEvidencePacket packet, CancellationToken cancellationToken = default) =>
        Task.FromResult(new EvidenceBackedRevisionResult(LlmEvidenceRemediationAgent.IsAbstention(sourceAnswer) ? $"根據目前已驗證證據，可對問題作有條件分析：[1] {packet.Evidence[0].Content}" : $"{sourceAnswer}\n\n補充證據：[1] {packet.Evidence[0].Content}", "已依驗證證據補強回答。", packet.Claims.Where(x => x.Status == "Supported").Select(x => x.ClaimText).ToList(), ["未提供精確預測時，結論僅代表方向性分析。"]));

    internal static IReadOnlyList<string> ExtractNumbers(string value) =>
        System.Text.RegularExpressions.Regex.Matches(value, @"-?\d+(?:\.\d+)?%?").Select(match => match.Value).Distinct().ToList();
}
