using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EquityLens.Api.Services.Agents.Loop;

/// <summary>比對答案數字與同句引用；只證明數值出現，不取代 Critic 的語義驗證。</summary>
internal static partial class ResearchNumberTraceability
{
    internal sealed record Result(bool Blocking, IReadOnlyList<string> Warnings);
    private sealed record Number(decimal Value, string Kind);

    internal static Result Check(string answer, IReadOnlyList<JsonObject> citations)
    {
        var total = 0; var missing = 0;
        var warnings = new HashSet<string>();
        foreach (var sentence in Regex.Split(answer.Normalize(NormalizationForm.FormKC), @"[。！？!?；;\n]"))
        {
            var numbers = Extract(sentence);
            if (numbers.Count == 0) continue;
            var ids = Reference().Matches(sentence).Select(x => int.TryParse(x.Groups[1].Value, out var id) ? id : -1).ToHashSet();
            if (ids.Count == 0) warnings.Add("UncitedNumber");
            var quotes = citations.Where(x => ids.Contains(x["citationIndex"]?.GetValue<int>() ?? -1))
                .SelectMany(x => Extract(x["quoteText"]?.GetValue<string>() ?? "")).ToList();
            foreach (var number in numbers)
            {
                total++;
                if (quotes.Any(q => q.Kind == number.Kind && Math.Sign(q.Value) == Math.Sign(number.Value)
                    && Math.Abs(q.Value - number.Value) <= (q.Kind == "plain" ? 0 : Math.Abs(q.Value) * .005m))) continue;
                missing++; warnings.Add("UntraceableNumber");
            }
        }
        return new(total > 0 && missing * 2 > total, warnings.ToArray());
    }

    private static List<Number> Extract(string text)
    {
        text = Reference().Replace(text.Normalize(NormalizationForm.FormKC).Replace('−', '-'), "");
        var result = new List<Number>();
        foreach (Match m in Numeric().Matches(text))
        {
            if (!decimal.TryParse(m.Groups[1].Value.Replace(",", ""), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var value)) continue;
            var mult = m.Groups[2].Value; var unit = m.Groups[3].Value;
            var kind = unit is "%" or "百分點" or "個百分點" ? "percent" : unit == "倍" ? "multiple"
                : unit is "元" or "美元" || mult.Length > 0 ? "money" : "plain";
            var scale = mult switch { "兆" => 1_000_000_000_000m, "億" => 100_000_000m, "萬" => 10_000m, "千" => 1000m, _ => 1m };
            try { result.Add(new(kind == "percent" ? value : value * scale, kind)); }
            catch (OverflowException) { /* 超出 decimal 範圍不進入計算。 */ }
        }
        return result;
    }

    [GeneratedRegex(@"\[(\d+)\]")]
    private static partial Regex Reference();
    [GeneratedRegex(@"([-+]?\d{1,3}(?:,\d{3})+(?:\.\d+)?|[-+]?\d+(?:\.\d+)?)\s*(兆|億|萬|千)?\s*(%|個百分點|百分點|美元|元|倍)?")]
    private static partial Regex Numeric();
}
