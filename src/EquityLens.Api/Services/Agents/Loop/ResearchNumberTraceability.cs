using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EquityLens.Api.Services.Agents.Loop;

/// <summary>比對答案數字與同句引用；只證明數值出現，不取代 Critic 的語義驗證。</summary>
internal static partial class ResearchNumberTraceability
{
    internal sealed record Result(bool Blocking, IReadOnlyList<string> Warnings);
    private sealed record Number(decimal Value, string Kind, bool AllowUnitlessMoney = true, int Offset = -1);

    internal static Result Check(string answer, IReadOnlyList<JsonObject> citations, string? question = null, string? ticker = null)
    {
        var questionNumbers = Extract(question ?? "");
        var total = 0; var missing = 0; var missingMoney = false; var invalidRatio = false;
        var warnings = new HashSet<string>();
        var derived = new List<(HashSet<int> Sources, Number Value)>();
        // 常見「句子。[1]」格式仍屬同句引用。
        answer = Regex.Replace(answer.Normalize(NormalizationForm.FormKC), @"([。！？!?；;])\s*((?:\[\d+\])+)", "$2$1");
        // 分號分隔的是同一句的子句；句尾引用涵蓋整句，句號與換行仍是引用邊界。
        foreach (var sentence in Regex.Split(answer, @"[。！？!?\n]"))
        {
            var contextual = Regex.Replace(sentence, @"(?<!\d)(?:19|20)\d{2}[-/年]\d{1,2}[-/月]\d{1,2}日?(?!\d)", m =>
            {
                var date = m.Value.Replace('年', '/').Replace('月', '/').Replace("日", "");
                return DateOnly.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ? "日期" : m.Value;
            });
            contextual = Regex.Replace(contextual, @"(?<!\d)(?:19|20)\d{2}\s*年", "年度");
            var numbers = Extract(contextual).Where(n =>
                !(n.Kind == "plain" && decimal.TryParse(ticker, out var code) && n.Value == code)
                && !questionNumbers.Any(q => q.Value == n.Value && q.Kind == n.Kind)).ToList();
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
            // 已驗證的結果只可在再次引用相同來源的句子重用，不能跨來源替模型背書。
            quotes.AddRange(derived.Where(x => x.Sources.IsSubsetOf(ids)).Select(x => x.Value));
            void Record(Number value)
            {
                quotes.Add(value);
                derived.Add((ids, value));
            }
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
                        Record(new(claimed.Value, "percent"));
                        warnings.Add("VerifiedRatioExpression");
                    }
                }
                catch (OverflowException) { }
                if (!verified) { invalidRatio = true; warnings.Add("InvalidRatioExpression"); }
            }
            // 「由舊值增／降至新值，年增率 p%」明示了運算元；用同句來源重算，不接受模型自報結果。
            foreach (Match expression in GrowthExpression().Matches(sentence))
            {
                var oldValue = Extract(expression.Groups["old"].Value).SingleOrDefault();
                var newValue = Extract(expression.Groups["new"].Value).SingleOrDefault();
                var verified = false;
                if (oldValue is not null && newValue is not null && oldValue.Value > 0 && oldValue.Kind == newValue.Kind
                    && quotes.Any(q => q.Kind == oldValue.Kind && q.Value == oldValue.Value)
                    && quotes.Any(q => q.Kind == newValue.Kind && q.Value == newValue.Value))
                {
                    try
                    {
                        var calculated = (newValue.Value / oldValue.Value - 1m) * 100m;
                        var claimed = Extract(expression.Groups["result"].Value).Single();
                        verified = Math.Abs(calculated - claimed.Value) <= Math.Abs(calculated) * .005m;
                        if (verified) { Record(new(claimed.Value, "percent")); warnings.Add("VerifiedGrowthExpression"); }
                    }
                    catch (OverflowException) { }
                }
                if (!verified) { invalidRatio = true; warnings.Add("UntraceableGrowthExpression"); }
            }
            // 差額必須明示兩個有來源的同單位運算元。百分比相減的結果只能是百分點。
            foreach (Match expression in DifferenceExpression().Matches(sentence))
            {
                var left = Extract(expression.Groups["left"].Value).SingleOrDefault();
                var right = Extract(expression.Groups["right"].Value).SingleOrDefault();
                var claimed = Extract(expression.Groups["result"].Value).SingleOrDefault();
                var verified = false;
                if (left is not null && right is not null && claimed is not null && left.Kind == right.Kind
                    && claimed.Kind == left.Kind && left.Kind != "plain"
                    && (left.Kind != "percent" || expression.Groups["result"].Value.Contains("百分點"))
                    && quotes.Any(q => q.Kind == left.Kind && q.Value == left.Value)
                    && quotes.Any(q => q.Kind == right.Kind && q.Value == right.Value))
                {
                    try
                    {
                        var calculated = left.Value - right.Value;
                        verified = Math.Abs(calculated - claimed.Value) <= Math.Abs(calculated) * .005m;
                        if (verified) { Record(claimed); warnings.Add("VerifiedDifferenceExpression"); }
                    }
                    catch (OverflowException) { }
                }
                if (!verified) { invalidRatio = true; warnings.Add("UntraceableDifferenceExpression"); }
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
        text = Reference().Replace(text.Normalize(NormalizationForm.FormKC).Replace('−', '-'), "");
        // 僅修復 EPS 標籤後連續的兩位小數表格欄位；不對一般數字猜測切割。
        // 完整匹配至少兩欄，保留兩位精度，移除原先貪婪解析出的假小數。
        text = Regex.Replace(text, @"(?<label>EPS\s*\(NT Dollar\)\s*)(?<cells>(?:\d+\.\d{2}){2,})(?![\d.])", m =>
            m.Groups["label"].Value + Regex.Replace(m.Groups["cells"].Value, @"\d+?\.\d{2}", "$0 "), RegexOptions.IgnoreCase);
        var numbers = Extract(text);
        // 匯率報價的分母明確是每一美元，不能把其他語境中的 1 一概忽略。
        if (Regex.IsMatch(text, @"(?:NT\$|NTD|新[臺台]幣)\s*/\s*(?:US\$|USD|美元)|(?:US\$|USD)\s*/\s*(?:NT\$|NTD)", RegexOptions.IgnoreCase))
            numbers.Add(new(1m, "money"));
        // 股數的單位常在列名，不能沿用財報整頁的仟元，也不能擴散到下一個 EPS／金額列。
        var shareRanges = ShareHeaderValues().Matches(text).Select(m => m.Groups["values"]).ToArray();
        var shareNumbers = numbers.Where(n => n.Kind == "plain"
            && shareRanges.Any(r => n.Offset >= r.Index && n.Offset < r.Index + r.Length)).ToArray();
        var shareOffsets = shareNumbers.Select(n => n.Offset).ToHashSet();
        numbers = numbers.Select(n => shareOffsets.Contains(n.Offset)
            ? n with { AllowUnitlessMoney = false } : n).ToList();
        foreach (var number in shareNumbers)
        {
            try { numbers.Add(new(number.Value * 1000m, "shares", false)); }
            catch (OverflowException) { }
        }
        // 財報通常在表頭一次宣告單位，欄位中的數字不重複標示。
        var compact = Regex.Replace(text.Normalize(NormalizationForm.FormKC), @"\s+", "");
        var header = Regex.Match(compact[..Math.Min(compact.Length, 800)], @"(?:新台幣|新臺幣|(?:收入|金額|單位[:：]?)\(?)(十億|百萬|仟|千|億|萬)元");
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
        foreach (var number in tableAmounts.Where(x => x.Kind == "plain" && !shareOffsets.Contains(x.Offset)))
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
            try { result.Add(new(kind == "percent" ? value : value * scale, kind, Offset: m.Index)); }
            catch (OverflowException) { /* 超出 decimal 範圍不進入計算。 */ }
        }
        return result;
    }

    // 數字後允許單位；除號後只允許短標籤，必須有等號及百分比結果。
    [GeneratedRegex(@"(?<left>[-+]?\d[\d,]*(?:\.\d+)?\s*(?:十億|百萬|兆|億|萬|千|仟)?\s*(?:美元|元|股)?)\s*(?:÷|除以|/)\s*[^\d\r\n=≈]{0,40}?(?<right>[-+]?\d[\d,]*(?:\.\d+)?\s*(?:十億|百萬|兆|億|萬|千|仟)?\s*(?:美元|元|股)?)\s*(?:(?:×|\*)\s*100\s*)?(?:=|≈|約為|等於)\s*(?<result>[-+]?\d+(?:\.\d+)?\s*%)")]
    private static partial Regex RatioExpression();

    [GeneratedRegex(@"由\s*(?<old>[-+]?\d[\d,]*(?:\.\d+)?\s*(?:十億|百萬|兆|億|萬|千|仟)?\s*(?:美元|元|股)?)\s*(?:增至|降至|成長至|下降至|至)\s*(?<new>[-+]?\d[\d,]*(?:\.\d+)?\s*(?:十億|百萬|兆|億|萬|千|仟)?\s*(?:美元|元|股)?)\s*[，,]?\s*年增率\s*(?:約為|約|為|=|≈)?\s*(?<result>[-+]?\d+(?:\.\d+)?\s*%)")]
    private static partial Regex GrowthExpression();

    [GeneratedRegex(@"(?<left>[-+]?\d[\d,]*(?:\.\d+)?\s*(?:十億|百萬|兆|億|萬|千|仟)?\s*(?:美元|元|股|%))\s*(?:−|-|減去)\s*(?<right>[-+]?\d[\d,]*(?:\.\d+)?\s*(?:十億|百萬|兆|億|萬|千|仟)?\s*(?:美元|元|股|%))\s*(?:=|≈|約為|等於)\s*(?<result>[-+]?\d[\d,]*(?:\.\d+)?\s*(?:十億|百萬|兆|億|萬|千|仟)?\s*(?:個百分點|百分點|美元|元|股|%))")]
    private static partial Regex DifferenceExpression();

    [GeneratedRegex(@"\[(\d+)\]")]
    private static partial Regex Reference();
    [GeneratedRegex(@"\((?:仟|千)股\)\s*(?<values>(?:\$?\s*[-+]?\d[\d,]*(?:\.\d+)?[\s|]*)+)")]
    private static partial Regex ShareHeaderValues();
    [GeneratedRegex(@"([-+]?\d{1,3}(?:,\d{3})+(?:\.\d+)?|[-+]?\d+(?:\.\d+)?)\s*(十億|百萬|兆|億|萬|千|仟)?\s*(%|個百分點|百分點|美元|元(?!件)|倍|股(?!數|東|份))?")]
    private static partial Regex Numeric();
}
