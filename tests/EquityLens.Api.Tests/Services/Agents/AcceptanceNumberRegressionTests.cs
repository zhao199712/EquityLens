using System.Text.Json;
using EquityLens.Api.Services.Agents;
using System.Text.Json.Nodes;
using EquityLens.Api.Services.Agents.Loop;
namespace EquityLens.Api.Tests.Services.Agents;
public class AcceptanceNumberRegressionTests
{
    [Fact]
    public void QualitativeFollowupDoesNotCompareNumericRiskBases()
    {
        var facts = new[] {
            new LoopDataFact("P1", "risk", "get_risk_report", new JsonObject { ["status"] = "CacheMiss", ["volatility"] = .2453m }),
            new LoopDataFact("P2", "scenario", "simulate_weight_scenario", new JsonObject { ["baseline"] = .2433m, ["after"] = .1827m }) };
        const string text = "風險報告原為 cache miss，數值為本次補算 [P1]。所有工具皆為唯讀，未調整任何持倉。若您能提供可接受的波動或回撤上限，我可再比較其他情境。";
        Assert.Empty(PortfolioQualityGateNodeHandler.Check(new(text, ["P1"]), facts));
        Assert.Empty(PortfolioQualityGateNodeHandler.Check(new("原組合年化波動為 24.53% [P1]", ["P1"]), facts));
        Assert.Contains("MixedRiskBasis", PortfolioQualityGateNodeHandler.Check(new("調整前後波動 24.53% → 18.27% [P1][P2]", ["P1", "P2"]), facts));
    }
    [Fact]
    public void DateRepairPointsToExactSourceWithoutAcceptingUncitedOrInventedDates()
    {
        var facts = new[] {
            new LoopDataFact("P1", "snapshot", "get_portfolio_snapshot", new JsonObject { ["count"] = 3 }),
            new LoopDataFact("P2", "risk", "run_risk_analysis", new JsonObject { ["start"] = "2025-11-14", ["observations"] = 252 }) };
        var answer = new LoopAnswer("## 分析期間 2025-11-14，252 筆\n\n3 檔持股 [P1]", ["P1"]);
        var rejected = new List<string>();
        Assert.Contains("UntraceableNumbers", PortfolioQualityGateNodeHandler.Check(answer, facts, rejected));
        var hints = PortfolioQualityGateNodeHandler.RepairHints(rejected, facts, answer);
        Assert.Contains("2025-11-14", hints);
        Assert.Contains("[P2]", hints);
        Assert.Empty(PortfolioQualityGateNodeHandler.Check(answer with { Markdown = answer.Markdown.Replace("252 筆", "252 筆 [P2]") }, facts));
        Assert.Contains("UntraceableNumbers", PortfolioQualityGateNodeHandler.Check(answer with { Markdown = "期間 2025-11-15 [P2]" }, facts));
    }
    [Fact]
    public void RoundingRepairHintsStayWithTheClaimedSource()
    {
        var facts = new[] {
            new LoopDataFact("P1", "risk", "run_risk_analysis", new JsonObject { ["riskShare"] = .99774663m }),
            new LoopDataFact("P2", "scenario", "simulate_weight_scenario", new JsonObject { ["riskShare"] = .9978m }) };
        var hint = PortfolioQualityGateNodeHandler.RepairHints(["99.78%"], facts, new("風險貢獻 99.78% [P1]", ["P1"]));
        Assert.Contains("[P1]", hint);
        Assert.Contains("rounded=99.77", hint);
        Assert.DoesNotContain("[P2]", hint);
    }
    [Fact]
    public void TableHeaderUsesItsOwnRowsSources_WithoutBorrowingOtherTables()
    {
        var facts = new[] { new LoopDataFact("P1", "scenario", "simulate_weight_scenario", new JsonObject { ["weight"] = .3m, ["volatility"] = .0924m }) };
        const string markdown = "| 指標 | 減至 30% |\n|---|---|\n| 波動 | 9.24% [P1] |";
        Assert.Empty(PortfolioQualityGateNodeHandler.Check(new(markdown, ["P1"]), facts));
        Assert.Contains("UntraceableNumbers", PortfolioQualityGateNodeHandler.Check(new(markdown.Replace("30%", "60%"), ["P1"]), facts));
        Assert.Contains("UntraceableNumbers", PortfolioQualityGateNodeHandler.Check(new(markdown + "\n\n另一段 30%", ["P1"]), facts));
    }
    [Theory]
    [InlineData("EPS 為 22.08 元 [1]", false)]
    [InlineData("EPS 為 22.09 元 [1]", true)]
    [InlineData("EPS 為 22.0819 元 [1]", true)]
    public void GluedEpsCellsAreNotArbitraryDecimals(string answer, bool blocked)
    {
        var result = ResearchNumberTraceability.Check(answer, [new JsonObject { ["citationIndex"] = 1, ["quoteText"] = "EPS (NT Dollar)22.0819.5013.94" }]);
        Assert.Equal(blocked, result.Blocking);
    }
    [Fact]
    public void RoundedRatioPercentagePointsAndListLabelsHaveEvidence()
    {
        var facts = new[] { new LoopDataFact("P1", "key", "simulate_weight_scenario", new JsonObject { ["hhi"] = .43407m, ["annualizedVolatility"] = .3m, ["difference"] = -.0082m }) };
        Assert.Empty(PortfolioQualityGateNodeHandler.Check(new("## 2. 比較\n2. HHI 0.434、波動差 -0.82 個百分點 [P1]", ["P1"]), facts));
        Assert.NotEmpty(PortfolioQualityGateNodeHandler.Check(new("HHI 0.43、波動差 -8.2 個百分點 [P1]", ["P1"]), facts));
    }
    [Fact]
    public void ComparisonCannotBorrowAnotherRiskBasisEvenWhenEveryNumberExists()
    {
        var facts = new[] {
            new LoopDataFact("P1", "risk", "run_risk_analysis", new JsonObject { ["annualizedVolatility"] = .5424m }),
            new LoopDataFact("P2", "scenario", "simulate_weight_scenario", new JsonObject { ["baseline"] = .5194m, ["after"] = .4482m, ["difference"] = -.0712m }) };
        Assert.Contains("MixedRiskBasis", PortfolioQualityGateNodeHandler.Check(new("調整前後波動 54.24% → 44.82%，差 -7.12 個百分點 [P1][P2]", ["P1", "P2"]), facts));
        Assert.Empty(PortfolioQualityGateNodeHandler.Check(new("調整前後波動 51.94% → 44.82%，差 -7.12 個百分點 [P2]", ["P2"]), facts));
    }
    [Theory]
    [InlineData("Exchange rate (NT$/US$) 31.7", false)]
    [InlineData("Revenue 31.7", true)]
    public void DollarDenominatorRequiresExchangeRateContext(string quote, bool blocked)
    {
        var result = ResearchNumberTraceability.Check("31.7 新台幣兌 1 美元 [1]", [new JsonObject { ["citationIndex"] = 1, ["quoteText"] = quote }]);
        Assert.Equal(blocked, result.Blocking);
    }
    [Fact]
    public void LiveSyntheticAnswer_TableCitationDatesAndContextAreTraceable()
    {
        var fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "portfolio-table-answer.json")))!;
        var answer = fixture["answer"]!.Deserialize<LoopAnswer>(AgentNodeJson.SerializerOptions)!;
        var facts = fixture["facts"]!.Deserialize<List<LoopDataFact>>(AgentNodeJson.SerializerOptions)!;
        // The live answer used P8's 99.78% while citing P7, which rounds to 99.77%.
        // Preserve that true error, then verify only the corrected answer passes.
        Assert.Contains("UntraceableNumbers", PortfolioQualityGateNodeHandler.Check(answer, facts));
        answer = answer with { Markdown = answer.Markdown.Replace("99.78%", "99.77%") };
        var rejected = new List<string>();
        var issues = PortfolioQualityGateNodeHandler.Check(answer, facts, rejected);
        Assert.True(issues.Count == 0, string.Join(",", issues) + ": " + string.Join(",", rejected));
        Assert.NotEmpty(PortfolioQualityGateNodeHandler.Check(answer with { Markdown = answer.Markdown.Replace("18.27%", "19.27%") }, facts));
    }
    [Fact]
    public void UnicodeMinusAndExplicitSeparateBasisAreNotFalsePositives()
    {
        var facts = new[] {
            new LoopDataFact("P1", "risk", "run_risk_analysis", new JsonObject { ["value"] = -.0233m }),
            new LoopDataFact("P2", "scenario", "simulate_weight_scenario", new JsonObject { ["difference"] = -.0909m }) };
        Assert.Empty(PortfolioQualityGateNodeHandler.Check(new("差異 −9.09 個百分點 [P2]", ["P2"]), facts));
        Assert.Empty(PortfolioQualityGateNodeHandler.Check(new("VaR −2.33% [P1]，與情境計算基礎不同，不宜交叉比較。", ["P1"]), facts));
        Assert.Contains("MixedRiskBasis", PortfolioQualityGateNodeHandler.Check(new("VaR −2.33% → −9.09%，計算基礎不同，不宜交叉比較 [P1][P2]", ["P1","P2"]), facts));
    }
}
