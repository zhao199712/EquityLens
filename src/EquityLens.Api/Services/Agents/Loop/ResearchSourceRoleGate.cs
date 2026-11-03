using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EquityLens.Api.Services.Ai.Retrieval;

namespace EquityLens.Api.Services.Agents.Loop;

/// <summary>只攔截可由引用中繼資料或明確頁面標記判定的來源錯置，語意充分性仍由評論檢查。</summary>
internal static class ResearchSourceRoleGate
{
    internal static IEnumerable<string> Check(string answer, string question, IReadOnlyList<JsonObject> citations)
    {
        var risk = new IntentDetector().Detect(question).Selected == ResearchQuestionIntent.Risk;
        foreach (var sentence in Regex.Split(answer, @"[。！？\n]"))
        {
            var ids = Regex.Matches(sentence, @"\[(\d+)\]").Select(x => int.Parse(x.Groups[1].Value)).ToHashSet();
            var used = citations.Where(x => ids.Contains(x["citationIndex"]?.GetValue<int>() ?? -1)).ToList();
            if (used.Count == 0) continue;
            if (Regex.IsMatch(sentence, @"(?:法說會|earnings call).{0,12}(?:提到|指出|揭露|表示|stated|mentioned)", RegexOptions.IgnoreCase)
                && used.All(x => x["documentType"]?.GetValue<string>() == "AnnualReport"))
                yield return "SourceAttributionMismatch";
            foreach (var source in used)
            {
                var quote = source["quoteText"]?.GetValue<string>() ?? "";
                // 只對明確起始標題下判斷，避免正文提到「目錄」就被誤擋。
                if (Regex.IsMatch(quote, @"^\s*(?:目\s*錄|封面|Table of Contents|Cover)(?:\s|[:：]|$)", RegexOptions.IgnoreCase))
                    yield return "NonSubstantiveSourcePage";
                if (risk && Regex.IsMatch(quote, @"^\s*(?:合併)?(?:資產負債表|綜合損益表|損益表|現金流量表)|^\s*Consolidated (?:Balance Sheets|Statements of (?:Income|Cash Flows))", RegexOptions.IgnoreCase)
                    && new IntentDetector().Detect(quote).Selected != ResearchQuestionIntent.Risk)
                    yield return "FinancialTableNotRiskEvidence";
            }
        }
    }
}
