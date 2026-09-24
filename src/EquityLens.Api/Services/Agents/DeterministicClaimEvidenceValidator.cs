using System.Globalization;
using System.Text.RegularExpressions;

namespace EquityLens.Api.Services.Agents;

public static partial class DeterministicClaimEvidenceValidator
{
    [GeneratedRegex(@"(?<![\d.])\d[\d,]*(?:\.\d+)?(?![\d.])", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();
    [GeneratedRegex(@"(?<!\d)20\d{2}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex YearPattern();
    [GeneratedRegex(@"(?:Q\s*([1-4])|第\s*([一二三四1-4])\s*季)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuarterPattern();
    [GeneratedRegex(@"(?:股票代號|ticker\s*[:：]?|代碼\s*[:：]?)\s*(\d{4,5})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TickerPattern();
    [GeneratedRegex(@"(?:個?百分點|percentage\s*points?|\bpp\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PointPattern();

    public static IReadOnlyList<string> Validate(EvidenceClaim claim, IReadOnlyList<RemediationEvidenceItem> selected)
    {
        var errors = new List<string>();
        var text = string.Join(" ", selected.Select(x => x.Content));
        var evidenceNumbers = NumberPattern().Matches(text).Select(x => Normalize(x.Value)).ToHashSet();
        foreach (var value in claim.NumericValues.Distinct())
        {
            var number = NumberPattern().Match(value);
            if (number.Success && !evidenceNumbers.Contains(Normalize(number.Value)))
                errors.Add($"Numeric value '{value}' is not present as a complete number in mapped evidence.");
        }

        // Only reject a period when both sides explicitly identify a single, different period.
        var claimYears = YearPattern().Matches(claim.Text).Select(x => x.Value).Distinct().ToList();
        var evidenceYears = YearPattern().Matches(text).Select(x => x.Value).Distinct().ToList();
        if (claimYears.Count == 1 && evidenceYears.Count == 1 && claimYears[0] != evidenceYears[0])
            errors.Add("The cited evidence explicitly identifies a different year.");
        var claimQuarters = QuarterPattern().Matches(claim.Text).Select(x => Quarter(x)).Distinct().ToList();
        var evidenceQuarters = QuarterPattern().Matches(text).Select(x => Quarter(x)).Distinct().ToList();
        if (claimQuarters.Count == 1 && evidenceQuarters.Count == 1 && claimQuarters[0] != evidenceQuarters[0])
            errors.Add("The cited evidence explicitly identifies a different quarter.");

        // A title's explicit ticker is provenance; free text may compare several companies.
        var claimTickers = TickerPattern().Matches(claim.Text).Select(x => x.Groups[1].Value).Where(NotYear).Distinct().ToList();
        var titleTickers = selected.SelectMany(x => TickerPattern().Matches(x.Title ?? "").Select(m => m.Groups[1].Value))
            .Where(NotYear).Distinct().ToList();
        if (claimTickers.Count == 1 && titleTickers.Count == 1 && claimTickers[0] != titleTickers[0])
            errors.Add("The cited document title explicitly identifies a different security ticker.");

        foreach (var value in claim.NumericValues)
        {
            var number = NumberPattern().Match(value);
            if (!number.Success) continue;
            var normalized = Normalize(number.Value);
            var claimUnit = Unit(value);
            if (claimUnit == "none") claimUnit = UnitNear(claim.Text, normalized);
            if (claimUnit == "none") continue;
            var evidenceUnits = NumberPattern().Matches(text).Where(x => Normalize(x.Value) == normalized)
                .Select(x => UnitAt(text, x.Index, x.Length)).Where(x => x != "none").Distinct().ToList();
            if (evidenceUnits.Count == 1 && evidenceUnits[0] != claimUnit)
                errors.Add($"Numeric value '{value}' has an explicit unit mismatch in mapped evidence.");
        }

        var claimDelta = PercentagePointDelta(claim.Text);
        if (claimDelta.HasValue && claimDelta.Value.Expected != claimDelta.Value.Stated)
            errors.Add("Claim's percentage-point arithmetic is inconsistent.");
        var evidenceDelta = PercentagePointDelta(text);
        if (evidenceDelta.HasValue && evidenceDelta.Value.Expected != evidenceDelta.Value.Stated)
            errors.Add("Cited evidence's percentage-point arithmetic is inconsistent.");
        return errors.Distinct().ToList();
    }

    private static string Normalize(string number) =>
        decimal.TryParse(number.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value.ToString(CultureInfo.InvariantCulture) : number;
    private static string Quarter(Match match) => match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value switch
    {
        "一" => "1", "二" => "2", "三" => "3", "四" => "4", var value => value
    };
    private static bool NotYear(string value) => !int.TryParse(value, out var number) || number is < 2000 or > 2099;
    private static string Unit(string value) => PointPattern().IsMatch(value) ? "point"
        : value.Contains('%') || value.Contains('％') ? "percent"
        : Regex.IsMatch(value, @"(?:USD|TWD|NT\$|US\$|\$|元)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ? "currency" : "none";
    private static string UnitNear(string text, string normalized)
    {
        var matches = NumberPattern().Matches(text).Where(x => Normalize(x.Value) == normalized).ToList();
        var units = matches.Select(x => UnitAt(text, x.Index, x.Length)).Where(x => x != "none").Distinct().ToList();
        return units.Count == 1 ? units[0] : "none";
    }
    private static string UnitAt(string text, int index, int length)
    {
        var before = text[Math.Max(0, index - 4)..index];
        var after = text[(index + length)..Math.Min(text.Length, index + length + 20)];
        if (Regex.IsMatch(after, @"^\s*(?:個?百分點|percentage\s*points?|pp\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "point";
        if (after.TrimStart().StartsWith('%') || after.TrimStart().StartsWith('％')) return "percent";
        return Regex.IsMatch(before, @"(?:USD|TWD|NT\$|US\$|\$)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            || Regex.IsMatch(after, @"^\s*(?:元|美元|新台幣)", RegexOptions.CultureInvariant) ? "currency" : "none";
    }
    private static (decimal Expected, decimal Stated)? PercentagePointDelta(string text)
    {
        if (!PointPattern().IsMatch(text)) return null;
        var transition = Regex.Match(text,
            @"(?:從|由|from)\s*(?<a>\d+(?:\.\d+)?)\s*[%％]\s*(?:上升|增加|下降|降低|升至|降至|至|到|to|→|->)\s*(?<b>\d+(?:\.\d+)?)\s*[%％]",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!transition.Success) return null;
        var tail = text[transition.Index..Math.Min(text.Length, transition.Index + transition.Length + 40)];
        var points = Regex.Matches(tail, @"(?<![\d.])(?<n>\d+(?:\.\d+)?)\s*(?:個?百分點|percentage\s*points?|pp\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (points.Count != 1) return null;
        var start = decimal.Parse(transition.Groups["a"].Value, CultureInfo.InvariantCulture);
        var end = decimal.Parse(transition.Groups["b"].Value, CultureInfo.InvariantCulture);
        return (Math.Abs(end - start), decimal.Parse(points[0].Groups["n"].Value, CultureInfo.InvariantCulture));
    }
}
