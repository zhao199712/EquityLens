using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EquityLens.Api.Services.Agents.Loop;

/// <summary>比對答案數字與同句引用；只證明數值出現，不取代 Critic 的語義驗證。</summary>
internal static partial class ResearchNumberTraceability
{
    internal sealed record Result(bool Blocking, IReadOnlyList<string> Warnings);
    private sealed record Number(decimal Value, string Kind, bool AllowUnitlessMoney = true);

    internal static Result Check(string answer, IReadOnlyList<JsonObject> citations)
    {
        var total = 0; var missing = 0; var missingMoney = false; var invalidRatio = false;
        var warnings = new HashSet<string>();
        // 常見「句子。[1]」格式仍屬同句引用。
        answer = Regex.Replace(answer.Normalize(NormalizationForm.FormKC), @"([。！？!?；;])\s*((?:\[\d+\])+)", "$2$1");
        foreach (var sentence in Regex.Split(answer, @"[。！？!?；;\n]"))
        {
            var numbers = Extract(sentence);
            if (numbers.Count == 0) continue;
            var ids = Reference().Matches(sentence).Select(x => int.TryParse(x.Groups[1].Value, out var id) ? id : -1).ToHashSet();
            if (ids.Count == 0)
            {
                warnings.Add("UncitedNumber");
                warnings.Add("AddCitationToSentence: " + sentence.Trim()[..Math.Min(160, sentence.Trim().Length)]);
            }
            var quotes = citations.Where(x => ids.Contains(x["citationIndex"]?.GetValue<int>() ?? -1))
                .SelectMany(x => ExtractQuote(x["quoteText"]?.GetValue<string>() ?? "")
                    .Concat(Extract(x["title"]?.GetValue<string>() ?? "").Select(n => n with { AllowUnitlessMoney = false }))).ToList();
            // 只驗證明示的百分比除法；兩個運算元必須來自同句引用，不能用任意模型數值造出依據。
            foreach (Match expression in RatioExpression().Matches(sentence))
            {
                var verified = false;
                var left = Extract(expression.Groups["left"].Value).SingleOrDefault();
                var right = Extract(expression.Groups["right"].Value).SingleOrDefault();
                if (left is null || right is null || left.Kind != right.Kind || right.Value == 0)
                { invalidRatio = true; warnings.Add("InvalidRatioExpression"); continue; }
                // 運算元採精確比對，結果才容許既有的四捨五入誤差；單位不可用裸數字替代。
                if (!quotes.Any(q => q.Kind == left.Kind && q.Value == left.Value) ||
                    !quotes.Any(q => q.Kind == right.Kind && q.Value == right.Value))
                { invalidRatio = true; warnings.Add("UntraceableRatioOperands"); continue; }
                try
                {
                    var calculated = left.Value / right.Value * 100m;
                    var claimed = Extract(expression.Groups["result"].Value).Single();
                    verified = Math.Abs(calculated - claimed.Value) <= Math.Abs(calculated) * .005m;
                    if (verified)
                    {
                        quotes.Add(new(calculated, "percent"));
                        warnings.Add("VerifiedRatioExpression");
                    }
                }
                catch (OverflowException) { }
                if (!verified) { invalidRatio = true; warnings.Add("InvalidRatioExpression"); }
            }
            foreach (var number in numbers)
            {
                total++;
                if (quotes.Any(q => q.Kind == number.Kind && Math.Sign(q.Value) == Math.Sign(number.Value)
                    && Math.Abs(q.Value - number.Value) <= (q.Kind == "plain" ? 0 : Math.Abs(q.Value) * .005m))) continue;
                // 財報表格的單位常在欄名，數值本身沒有後綴；只能證明數值出現，單位語義仍交 Critic。
                if (number.Kind != "plain" && quotes.Any(q => q.Kind == "plain" && q.Value == number.Value && (number.Kind != "money" || q.AllowUnitlessMoney)))
                { warnings.Add("UnitContextRequiresReview"); continue; }
                missing++; warnings.Add("UntraceableNumber");
                if (number.Kind == "money" && ids.Count > 0) missingMoney = true;
                warnings.Add($"CheckNumber: {number.Value.ToString(CultureInfo.InvariantCulture)} ({number.Kind})");
            }
        }
        return new(invalidRatio || missingMoney || (total > 0 && missing * 2 > total), warnings.ToArray());
    }

    private static IReadOnlyList<Number> ExtractQuote(string text)
    {
        var numbers = Extract(text);
        // 財報通常在表頭一次宣告單位，欄位中的數字不重複標示。
        var compact = Regex.Replace(text.Normalize(NormalizationForm.FormKC), @"\s+", "");
        var header = Regex.Match(compact[..Math.Min(compact.Length, 800)], @"(?:新台幣|新臺幣)(十億|百萬|仟|千|億|萬)元");
        if (!header.Success) return numbers;
        var scale = header.Groups[1].Value switch
        {
            "十億" => 1_000_000_000m, "百萬" => 1_000_000m, "億" => 100_000_000m,
            "萬" => 10_000m, _ => 1000m
        };
        // 表頭常明確排除 EPS；每股盈餘區段不繼承仟元／十億元。
        var perShareSection = Regex.Match(text, @"(?:基\s*本|稀\s*釋)\s*每\s*股\s*盈\s*餘");
        var tableAmounts = perShareSection.Success ? Extract(text[..perShareSection.Index]) : numbers;
        var expanded = numbers.Select((n, i) => i < tableAmounts.Count ? n with { AllowUnitlessMoney = false } : n).ToList();
        foreach (var number in tableAmounts.Where(x => x.Kind == "plain"))
        {
            try { expanded.Add(new(number.Value * scale, "money")); }
            catch (OverflowException) { }
        }
        return expanded;
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
                : unit == "股" ? "shares" : unit is "元" or "美元" || mult.Length > 0 ? "money" : "plain";
            var scale = mult switch { "兆" => 1_000_000_000_000m, "十億" => 1_000_000_000m, "百萬" => 1_000_000m, "億" => 100_000_000m, "萬" => 10_000m, "千" or "仟" => 1000m, _ => 1m };
            try { result.Add(new(kind == "percent" ? value : value * scale, kind)); }
            catch (OverflowException) { /* 超出 decimal 範圍不進入計算。 */ }
        }
        return result;
    }

    // 數字後允許單位；除號後只允許短標籤，必須有等號及百分比結果。
    [GeneratedRegex(@"(?<left>[-+]?\d[\d,]*(?:\.\d+)?\s*(?:十億|百萬|兆|億|萬|千|仟)?\s*(?:美元|元|股)?)\s*(?:÷|除以|/)\s*[^\d\r\n=≈]{0,40}?(?<right>[-+]?\d[\d,]*(?:\.\d+)?\s*(?:十億|百萬|兆|億|萬|千|仟)?\s*(?:美元|元|股)?)\s*(?:(?:×|\*)\s*100\s*)?(?:=|≈|約為|等於)\s*(?<result>[-+]?\d+(?:\.\d+)?\s*%)")]
    private static partial Regex RatioExpression();

    [GeneratedRegex(@"\[(\d+)\]")]
    private static partial Regex Reference();
    [GeneratedRegex(@"([-+]?\d{1,3}(?:,\d{3})+(?:\.\d+)?|[-+]?\d+(?:\.\d+)?)\s*(十億|百萬|兆|億|萬|千|仟)?\s*(%|個百分點|百分點|美元|元|倍|股)?")]
    private static partial Regex Numeric();
}
