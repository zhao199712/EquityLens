using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EquityLens.Api.Services.Agents.Loop;

/// <summary>提交前以現有證據核對數字，並為明示缺項保留有限的補查機會。</summary>
internal static class ResearchAnswerRepair
{
    internal static string? Hint(LoopAnswer? answer, IReadOnlyList<LoopEvidence> ledger, string? question = null, string? ticker = null)
    {
        if (answer is null || ledger.Count == 0) return null;
        var markdown = ResearchCitationSyntax.Normalize(answer.Markdown);
        var citations = ledger.Select((e, i) => new JsonObject { ["citationIndex"] = i + 1,
            ["quoteText"] = e.Chunk.Result.Content, ["title"] = e.Chunk.Result.DocumentTitle,
            ["documentId"] = e.Chunk.Result.DocumentId.ToString(), ["pageNumber"] = e.Chunk.Result.PageNumber }).ToArray();
        var indexes = ledger.Select((e, i) => (e.Id, Index: i + 1)).ToDictionary(e => e.Id, e => e.Index);
        // 未知引用交最終來源檢查處理，不把它誤報為可重算的數值問題。
        if (Regex.Matches(markdown, @"\[(E\d+)\]").Any(m => !indexes.ContainsKey(m.Groups[1].Value))) return null;
        markdown = Regex.Replace(markdown, @"\[(E\d+)\]", m => indexes.TryGetValue(m.Groups[1].Value, out var i) ? $"[{i}]" : m.Value);
        var check = ResearchNumberTraceability.Check(markdown, citations, question, ticker);
        if (check.Blocking)
            return "數字核對未通過：" + string.Join(", ", check.Warnings) +
                "。依來源與顯示精度重算；單位缺失時讀表頭／相鄰續表並同句引用，非必要附加數字移除。保留已核實答案，不重做相同搜尋。";
        if (Regex.IsMatch(markdown, @"(?:無法|尚未|未能|不能)(?:可靠|據此|直接)?(?:核實|確認|報告|回答)|(?:未提供|未列|缺少).{0,30}(?:資料|數據|數值|指標)|需要.{0,30}(?:續頁|補查)", RegexOptions.IgnoreCase))
            return "答案仍有明示缺項。先檢查 evidenceIndex 與已有證據；仍有預算時補查相鄰續表或改用精確搜尋。確實不可得則保留已核實部分與查找限制；不猜值，也不為通過檢查刪除限制。";
        return null;
    }
}
