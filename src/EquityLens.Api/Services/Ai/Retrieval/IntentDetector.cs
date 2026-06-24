using System.Text.RegularExpressions;

namespace EquityLens.Api.Services.Ai.Retrieval;

public sealed class IntentDetector : IIntentDetector
{
    public IntentDetectionResult Detect(string question)
    {
        var normalized = question.ToLowerInvariant();
        var riskKeywords = new[] { "風險", "risk", "challenge", "uncertainty", "headwind", "不確定", "挑戰" };
        var financialKeywords = new[] { "營收", "revenue", "毛利", "gross margin", "operating margin", "eps", "每股盈餘", "現金流", "cash flow", "income statement", "balance sheet", "損益", "資產負債", "財務" };
        var outlookKeywords = new[] { "展望", "outlook", "guidance", "forecast", "management expects", "management expect", "future outlook", "business outlook", "管理層預期", "管理層展望" };

        var matchedRiskKeywords = FindMatches(normalized, riskKeywords);
        if (matchedRiskKeywords.Count > 0)
        {
            return new IntentDetectionResult(ResearchQuestionIntent.Risk, matchedRiskKeywords, 1.0);
        }

        var matchedFinancialKeywords = FindMatches(normalized, financialKeywords);
        if (matchedFinancialKeywords.Count > 0)
        {
            return new IntentDetectionResult(ResearchQuestionIntent.Financial, matchedFinancialKeywords, 1.0);
        }

        var matchedOutlookKeywords = FindMatches(normalized, outlookKeywords);
        if (matchedOutlookKeywords.Count > 0)
        {
            return new IntentDetectionResult(ResearchQuestionIntent.Outlook, matchedOutlookKeywords, 1.0);
        }

        return new IntentDetectionResult(ResearchQuestionIntent.General, [], 0.5);
    }

    private static IReadOnlyList<string> FindMatches(string value, IReadOnlyList<string> candidates)
    {
        return candidates
            .Where(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
