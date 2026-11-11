using EquityLens.Api.Contracts.Risk;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.Agents.Loop;

namespace EquityLens.Api.Tests.Services.Agents;

// 數值取自本機真實模型驗證的壓力測試結果；模型以「約虧 25.08%」與情境名稱作答時曾被誤判。
public sealed class PortfolioStressGateTests
{
    private static readonly LoopDataFact[] Facts = [new("P3", "stress", "run_stress_test", PortfolioLoopTools.StressEvidence(
        new PortfolioStressTestResponse(Guid.NewGuid(), new(2026, 11, 4), [
            new("rates_2022", "2022 升息熊市", "historical", "ready", "回放", new(2022, 1, 3), new(2022, 10, 14), -0.2507744733m,
                [new("2330", "台積電", "電子工業", 0.7902097902m, 1, 1, -0.3364685166m, -0.2658807159m),
                 new("2317", "鴻海", "電子工業", 0.2097902098m, 1, 1, 0.0720064228m, 0.0151062425m)], [new("電子工業", 1m, -0.2507744733m, -0.2507744733m)]),
            new("taiwan_strait_supply_chain", "台海供應鏈中斷", "hypothetical", "ready", "假設衝擊", null, null, -0.4290209790m,
                [new("2330", "台積電", "電子工業", 0.7902097902m, 1, 1, -0.45m, -0.3555944056m)], []),
            new("worst_20d", "資料期間最差 20 日", "historical", "ready", "回放", new(2020, 2, 21), new(2020, 3, 19), -0.3m, [], [])]),
        new(Guid.NewGuid(), "投組", "TWD", new(2025, 11, 10), new(2026, 11, 10), 2)))];

    [Theory]
    [InlineData("鴻海負貢獻 0.28 個百分點 [P1]。", false)]
    [InlineData("鴻海負貢獻 0.30 個百分點 [P1]。", true)]
    [InlineData("鴻海正貢獻 0.28 個百分點 [P1]。", true)]
    [InlineData("鴻海負貢獻 0.28 個百分點。", true)]
    public void AttributionLossMagnitude_RequiresNegativeEvidenceCorrectValueAndCitation(string markdown, bool rejected)
    {
        var issues = PortfolioQualityGateNodeHandler.Check(new(markdown, markdown.Contains("[P1]") ? ["P1"] : []),
            [new("P1", "attribution", "get_performance_attribution", new() { ["contribution"] = -0.0027690379146854895m })]);
        Assert.Equal(rejected, issues.Contains("UntraceableNumbers"));
    }

    [Theory]
    [InlineData("以目前持倉回放，2022 升息熊市約虧 25.08%；台海供應鏈中斷的假設情境約虧 42.90% [P3]。")]
    [InlineData("2022 情境下鴻海個別報酬為正，貢獻 +1.5106% [P3]。")]
    [InlineData("| 2022 升息熊市 | 歷史回放，2022-01-03 至 2022-10-14 | -25.0774% | 台積電：79.0210%／-33.6469%／-26.5881% [P3] |")]
    [InlineData("資料期間最差 20 日的總衝擊為 -30.00% [P3]。")]
    public void StressScenarioNamesAndLossMagnitudes_AreTraceable(string markdown)
        => Assert.Empty(PortfolioQualityGateNodeHandler.Check(new(markdown, ["P3"]), Facts));

    [Theory]
    [InlineData("2022 升息熊市約虧 26.00% [P3]。")]       // 數值錯誤
    [InlineData("2022 升息熊市約賺 25.08% [P3]。")]       // 方向錯誤：沒有損失字樣不能去掉負號
    [InlineData("2021 情境下投組虧 25.08% [P3]。")]       // 不是工具情境的年份
    [InlineData("2022 升息熊市約虧 25.08%。")]            // 未引用壓力測試
    public void StressClaims_StillRejectWrongValuesDirectionsAndUncitedNames(string markdown)
        => Assert.Contains("UntraceableNumbers", PortfolioQualityGateNodeHandler.Check(new(markdown, markdown.Contains("[P3]") ? ["P3"] : []), Facts));
}
