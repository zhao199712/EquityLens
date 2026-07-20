namespace EquityLens.Api.Services.Agents;

public sealed record ClaimSetValidationInput(
    string Question,
    string SourceAnswer,
    IReadOnlyList<CriticFinding> CriticFindings,
    string InvestigationMode,
    IReadOnlyList<EvidenceClaim> Claims);

public sealed record ClaimSetValidationResult(
    bool IsValid,
    double Coverage,
    IReadOnlyList<string> RequiredDimensions,
    IReadOnlyList<string> CoveredDimensions,
    IReadOnlyList<string> MissingDimensions,
    IReadOnlyList<string> Errors,
    string Resolution = "Accepted");

public interface IClaimSetValidator
{
    ClaimSetValidationResult Validate(ClaimSetValidationInput input);
}

public sealed class ClaimSetValidator : IClaimSetValidator
{
    private static readonly string[] WeakClaimMarkers = ["可作為預測", "可作為評估", "可作為參考", "值得研究", "需要調查"];

    public ClaimSetValidationResult Validate(ClaimSetValidationInput input)
    {
        var required = RequiredDimensions(input.Question, input.InvestigationMode);
        var domain = input.Claims.Where(x => x.ClaimType != EvidenceClaimTypes.Answerability && !LlmEvidenceRemediationAgent.IsMetaClaim(x.Text)).ToList();
        var covered = required.Where(d => domain.Any(c => MatchesDimension(c, d))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var missing = required.Except(covered, StringComparer.OrdinalIgnoreCase).ToList();
        var errors = new List<string>();
        if (domain.Count == 0) errors.Add("Claim set contains no domain claims.");
        if (input.InvestigationMode == InvestigationModes.RecoverAnswer)
        {
            foreach (var type in new[] { EvidenceClaimTypes.Factual, EvidenceClaimTypes.Mechanism, EvidenceClaimTypes.Judgment })
                if (!domain.Any(x => x.ClaimType == type)) errors.Add($"Claim set is missing required claim type '{type}'.");
            if (domain.Any(x => string.IsNullOrWhiteSpace(x.ResearchDimension))) errors.Add("Every domain claim requires a researchDimension.");
            if (domain.Count > 0 && input.Claims.Count(x => x.ClaimType == EvidenceClaimTypes.Answerability || LlmEvidenceRemediationAgent.IsMetaClaim(x.Text)) >= domain.Count)
                errors.Add("Answerability or meta claims dominate the claim set.");
            if (domain.Any(x => WeakClaimMarkers.Any(marker => x.Text.Contains(marker, StringComparison.OrdinalIgnoreCase))))
                errors.Add("Claim set contains weak research propositions that do not directly answer the question.");
            if (missing.Count > 0) errors.Add($"Claim set is missing research dimensions: {string.Join(", ", missing)}.");
        }
        var coverage = required.Count == 0 ? 1 : Math.Round((double)covered.Count / required.Count, 3);
        return new(errors.Count == 0, coverage, required, covered, missing, errors);
    }

    private static IReadOnlyList<string> RequiredDimensions(string question, string mode)
    {
        if (mode != InvestigationModes.RecoverAnswer) return [];
        if (ContainsAny(question, "資本支出", "capex", "自由現金流", "FCF", "股東回報", "股利"))
            return ["CapEx guidance", "FCF impact", "Cash flow coverage", "Depreciation impact", "Shareholder returns", "Growth offset"];
        return ["Key facts", "Mechanism", "Conditional judgment"];
    }

    private static bool MatchesDimension(EvidenceClaim claim, string required)
    {
        var dimension = claim.ResearchDimension ?? string.Empty;
        if (dimension.Equals(required, StringComparison.OrdinalIgnoreCase)) return true;
        var text = $"{dimension} {claim.Text}";
        return required switch
        {
            "CapEx guidance" => ContainsAny(text, "資本支出", "capex", "指引", "規模", "期間"),
            "FCF impact" => ContainsAny(text, "自由現金流", "FCF") && ContainsAny(text, "影響", "壓縮", "減少"),
            "Cash flow coverage" => ContainsAny(text, "營業現金流", "現金部位", "覆蓋"),
            "Depreciation impact" => ContainsAny(text, "折舊", "depreciation"),
            "Shareholder returns" => ContainsAny(text, "股東回報", "股利", "庫藏股", "回購"),
            "Growth offset" => ContainsAny(text, "成長", "產能", "營收") && ContainsAny(text, "抵銷", "轉化", "機會"),
            "Key facts" => claim.ClaimType == EvidenceClaimTypes.Factual,
            "Mechanism" => claim.ClaimType == EvidenceClaimTypes.Mechanism,
            "Conditional judgment" => claim.ClaimType == EvidenceClaimTypes.Judgment,
            _ => false
        };
    }

    private static bool ContainsAny(string value, params string[] markers) => markers.Any(x => value.Contains(x, StringComparison.OrdinalIgnoreCase));
}

public interface IAnswerQualityValidator
{
    AnswerQualityValidationResult Validate(string answer, string investigationMode, IReadOnlyList<EvidenceClaim> claims, IReadOnlyList<string> requiredDimensions, IReadOnlySet<string> supportedClaimIds, IReadOnlySet<int> allowedEvidenceIndexes);
}

public sealed class AnswerQualityValidator : IAnswerQualityValidator
{
    public AnswerQualityValidationResult Validate(string answer, string investigationMode, IReadOnlyList<EvidenceClaim> claims, IReadOnlyList<string> requiredDimensions, IReadOnlySet<string> supportedClaimIds, IReadOnlySet<int> allowedEvidenceIndexes)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(answer)) errors.Add("Final answer is empty.");
        var cited = System.Text.RegularExpressions.Regex.Matches(answer, @"\[(\d+)\]").Select(x => int.Parse(x.Groups[1].Value)).ToList();
        if (cited.Count == 0 && allowedEvidenceIndexes.Count > 0) errors.Add("Final answer must cite validated evidence.");
        if (cited.Any(x => !allowedEvidenceIndexes.Contains(x))) errors.Add("Final answer cites evidence that was not validated.");
        if (investigationMode == InvestigationModes.RecoverAnswer && LlmEvidenceRemediationAgent.IsMetaClaim(answer) && answer.Length < 180)
            errors.Add("Recovered answer still primarily abstains despite available evidence.");
        if (errors.Count > 0) throw new AgentNodeException("claim_set_invalid", AgentNodeErrorCategories.ValidationFailure, string.Join(" ", errors), retryable: false);
        var answered = claims.Where(x => supportedClaimIds.Contains(x.Id)).Select(x => x.ResearchDimension ?? x.Text).Distinct().ToList();
        var missing = requiredDimensions.Except(answered, StringComparer.OrdinalIgnoreCase).ToList();
        var coverage = requiredDimensions.Count == 0 ? 1 : Math.Round((double)answered.Count(x => requiredDimensions.Contains(x, StringComparer.OrdinalIgnoreCase)) / requiredDimensions.Count, 3);
        return new(missing.Count == 0 ? "Complete" : "Partial", coverage, answered, missing, []);
    }
}
