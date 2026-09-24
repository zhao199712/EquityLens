using EquityLens.Api.Services.Agents;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class DeterministicClaimEvidenceValidatorTests
{
    [Fact]
    public void CompleteNumbers_PreventSubstringMatchesAndNormalizeThousands()
    {
        Assert.Contains(Errors("營收為20元", ["20"], "營收為2020元"), x => x.Contains("complete number"));
        Assert.Empty(Errors("營收為1,200元", ["1,200"], "營收為1200元"));
    }

    [Fact]
    public void ExplicitPeriodMismatch_IsRejected_ButMultiplePeriodsRemainAmbiguous()
    {
        Assert.Contains(Errors("2026年Q2毛利率上升", [], "2025年Q1毛利率上升"), x => x.Contains("year"));
        Assert.Contains(Errors("2026年Q2毛利率上升", [], "2025年Q1毛利率上升"), x => x.Contains("quarter"));
        Assert.Empty(Errors("2026年Q2毛利率上升", [], "比較2025年Q1與2026年Q2，毛利率上升"));
    }

    [Fact]
    public void ExplicitTitleTickerMismatch_IsRejected()
    {
        var claim = new EvidenceClaim("c", "股票代號2330的毛利率上升", []);
        var item = Evidence("毛利率上升") with { Title = "股票代號2454的財報" };
        Assert.Contains(DeterministicClaimEvidenceValidator.Validate(claim, [item]), x => x.Contains("ticker"));
    }

    [Fact]
    public void ExplicitUnitsAndPercentagePointArithmetic_AreChecked()
    {
        Assert.Contains(Errors("毛利率為48%", ["48%"], "毛利率為48美元"), x => x.Contains("unit mismatch"));
        Assert.Contains(Errors("毛利率從48%升至50%，增加3個百分點", [], "毛利率從48%升至50%"), x => x.Contains("arithmetic"));
        Assert.Empty(Errors("毛利率從48%升至50%，增加2個百分點", ["48%", "50%", "2"], "毛利率從48%升至50%，增加2個百分點"));
        Assert.Empty(Errors("毛利率48%，利用率50%，股息率增加3個百分點", [], "毛利率48%，利用率50%，股息率增加3個百分點"));
    }

    private static IReadOnlyList<string> Errors(string text, IReadOnlyList<string> numbers, string passage) =>
        DeterministicClaimEvidenceValidator.Validate(new EvidenceClaim("c", text, numbers), [Evidence(passage)]);
    private static RemediationEvidenceItem Evidence(string content) => new(1, "Local", null, null, null, content, 1);
}
