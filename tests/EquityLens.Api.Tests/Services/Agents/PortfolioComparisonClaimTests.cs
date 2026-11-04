using System.Text.Json;
using EquityLens.Api.Tests.Infrastructure;
using System.Text.Json.Nodes;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.Agents.Loop;

namespace EquityLens.Api.Tests.Services.Agents;

public class PortfolioComparisonClaimTests
{
    internal static (LoopAnswer Answer, List<LoopDataFact> Facts, PortfolioScenarioComparison Comparison) Fixture()
    {
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Services/Agents/Fixtures/etf-comparison-answer.json")))!;
        return (json["answer"]!.Deserialize<LoopAnswer>(AgentNodeJson.SerializerOptions)!,
            json["facts"]!.Deserialize<List<LoopDataFact>>(AgentNodeJson.SerializerOptions)!,
            json["comparison"]!.Deserialize<PortfolioScenarioComparison>(AgentNodeJson.SerializerOptions)!);
    }

    [Fact]
    public void SavedLiveAnswer_RejectsReversedVarComparisonDespiteCorrectNumbers()
    {
        var (answer, facts, _) = Fixture();
        Assert.Contains("ScenarioComparisonDirectionMismatch", PortfolioQualityGateNodeHandler.Check(answer, facts));
        var corrected = answer with { Markdown = answer.Markdown.Replace(
            "其 VaR 與期望短缺的改善幅度小於留現金", "其 VaR 改善幅度大於留現金，期望短缺的改善幅度小於留現金") };
        Assert.Empty(PortfolioQualityGateNodeHandler.Check(corrected, facts));
    }

    [Theory]
    [InlineData("0050 的 VaR 改善幅度大於現金 [P8]", true)]
    [InlineData("0050 的 VaR 改善幅度小於現金 [P8]", false)]
    [InlineData("0050 的期望短缺改善幅度小於現金 [P8]", true)]
    [InlineData("現金的 VaR 改善幅度小於 ETF [P8]", true)]
    [InlineData("ETF 的波動率改善幅度小於現金 [P8]", true)]
    [InlineData("ETF 的最大回撤改善幅度小於現金 [P8]", true)]
    [InlineData("ETF 的 VaR 高於現金 [P8]", true)]
    [InlineData("ETF 的 VaR 低於現金 [P8]", false)]
    [InlineData("ETF 的波動率高於現金 [P8]", true)]
    [InlineData("ETF 的 VaR 與 ES 改善幅度大於現金 [P8]", false)]
    [InlineData("希望波動率低於20%，這是目標而非計算結果。", true)]
    public void ChecksEachMetricAndSignedDirection(string text, bool valid)
    {
        var (_, facts, _) = Fixture();
        Assert.Equal(valid, PortfolioComparisonClaims.Check(text, facts).Count == 0);
    }

    [Theory]
    [InlineData("其 VaR 改善幅度大於現金 [P8]")]
    [InlineData("ETF 的 VaR 改善幅度大於現金 [P3]")]
    [InlineData("ETF 的 VaR 改善幅度大於現金")]
    [InlineData("ETF 的 VaR 改善幅度不小於現金 [P8]")]
    public void MissingSubjectOrEvidenceOrUnsupportedNegationRequestsClarification(string text)
    {
        var (_, facts, _) = Fixture();
        Assert.Contains("UnverifiedScenarioComparison", PortfolioComparisonClaims.Check(text, facts));
    }

    [Fact]
    public void MultipleEtfScenariosRequireExactName()
    {
        var (_, _, comparison) = Fixture();
        var etf = comparison.Scenarios[0];
        comparison = comparison with { Scenarios = [etf, etf with { Name = "另一減碼配置" }, comparison.Scenarios[1]] };
        var fact = new LoopDataFact("P1", "scenario", "simulate_weight_scenario", PortfolioLoopTools.ScenarioEvidence(comparison));
        Assert.Contains("UnverifiedScenarioComparison", PortfolioComparisonClaims.Check("ETF 的 VaR 改善幅度大於現金 [P1]", [fact]));
        Assert.Empty(PortfolioComparisonClaims.Check("另一減碼配置的 VaR 改善幅度大於現金 [P1]", [fact]));
    }

    [Theory]
    [InlineData("減碼至六成，餘額留現金", "減碼至四成，餘額留現金")]
    [InlineData("方案甲,現金", "方案乙;現金")]
    public void CanonicalStatementsWithPunctuationInNames_StillRequireExactEvidenceAndDirection(string first, string second)
    {
        var (_, _, comparison) = Fixture();
        comparison = comparison with { Scenarios = comparison.Scenarios.Select((s, i) => s with {
            Name = i == 0 ? first : second, RemainderPolicy = "cash", TransferToTicker = null
        }).ToArray() };
        var fact = new LoopDataFact("P1", "scenario", "simulate_weight_scenario", PortfolioLoopTools.ScenarioEvidence(comparison));
        foreach (var statement in PortfolioComparisonClaims.Statements(comparison))
        {
            Assert.Empty(PortfolioComparisonClaims.Check(statement + " [P1]", [fact]));
            var reversed = statement.Contains("大於") ? statement.Replace("大於", "小於") : statement.Replace("小於", "大於");
            Assert.Contains("ScenarioComparisonDirectionMismatch", PortfolioComparisonClaims.Check(reversed + " [P1]", [fact]));
            Assert.Contains("UnverifiedScenarioComparison", PortfolioComparisonClaims.Check(statement + " [P9]", [fact]));
            Assert.Contains("UnverifiedScenarioComparison", PortfolioComparisonClaims.Check(statement.Replace(second, "不存在的情境") + " [P1]", [fact]));
        }
    }

    [Fact]
    public void DerivedComparisonsHandleEqualAndWorseOutcomes()
    {
        var (_, _, comparison) = Fixture();
        var a = comparison.Scenarios[0]; var cash = comparison.Scenarios[1];
        comparison = comparison with { Scenarios = [a with { Metrics = cash.Metrics }, cash] };
        Assert.All(PortfolioComparisonClaims.Statements(comparison), text => Assert.Contains("改善幅度等於", text));
        var worse = comparison.Baseline!.Metrics! with { AnnualizedVolatility = .9m, HistoricalVaR = -.9m, ExpectedShortfall = -.95m, HypotheticalMaxDrawdown = -.99m };
        comparison = comparison with { Scenarios = [a with { Metrics = worse }, cash] };
        Assert.All(PortfolioComparisonClaims.Statements(comparison), text => Assert.Contains("改善幅度小於", text));
        // Statements derive from saved metrics, never trust a stale/inconsistent DifferenceFromBaseline.
        var evidence = PortfolioLoopTools.ScenarioEvidence(comparison);
        Assert.Equal(4, evidence["riskComparisonStatements"]!.AsArray().Count);
    }

    [Fact]
    public async Task FinalGateRemovesBadParagraphButKeepsVerifiedAdviceAndToolComparisons()
    {
        var (answer, facts, comparison) = Fixture();
        await using var db = RefactorTestDbContext.Create();
        var board = new JsonObject {
            [AgentBlackboardKeys.PortfolioContext] = JsonSerializer.SerializeToNode(new PortfolioDiagnosisContext(Guid.NewGuid(), "合成組合", "TWD", new(2025,11,3), new(2026,11,3), 3), AgentNodeJson.SerializerOptions),
            ["portfolioLoopAnswer"] = JsonSerializer.SerializeToNode(answer, AgentNodeJson.SerializerOptions),
            ["agentLoopFacts"] = JsonSerializer.SerializeToNode(facts, AgentNodeJson.SerializerOptions),
            [PortfolioScenarioCalculator.ResultKey] = JsonSerializer.SerializeToNode(comparison, AgentNodeJson.SerializerOptions)
        };
        var run = new EquityLens.Api.Data.Entities.AgentRun { Id = Guid.NewGuid(), BlackboardJson = board.ToJsonString() };
        var node = new EquityLens.Api.Data.Entities.AgentRunNode { Id = Guid.NewGuid(), AgentRunId = run.Id };
        await new PortfolioQualityGateNodeHandler().ExecuteAsync(new(db, run, node, (_, _, _, _, _) => { }));
        board = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        var output = board[AgentBlackboardKeys.PortfolioDiagnosisDraft]!.Deserialize<PortfolioDiagnosisOutput>(AgentNodeJson.SerializerOptions)!;
        Assert.False(board["portfolioQualityGate"]!["passed"]!.GetValue<bool>());
        Assert.Contains("ScenarioComparisonDirectionMismatch", output.QualityIssues!);
        Assert.DoesNotContain("其 VaR 與期望短缺", output.Interpretation);
        Assert.Contains("尚未取得您的風險偏好", output.Interpretation);
        Assert.Contains("VaR改善幅度大於", output.Summary);
        Assert.Contains("期望短缺改善幅度小於", output.Summary);
    }

    [Fact]
    public void PreviousFourteenCaseAnswersHaveNoNewComparisonFalsePositives()
    {
        var rows = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Services/Agents/Fixtures/round19-comparison-claims.json")))!.AsArray();
        Assert.Equal(10, rows.Count); // 原 14 題另 4 題走澄清，沒有模型投組報告。
        foreach (var row in rows)
        {
            var answer = row!["answer"]!.Deserialize<LoopAnswer>(AgentNodeJson.SerializerOptions)!;
            var facts = row["facts"]!.Deserialize<List<LoopDataFact>>(AgentNodeJson.SerializerOptions)!;
            var issues = PortfolioComparisonClaims.Check(answer.Markdown, facts);
            Assert.True(issues.Count == 0, row["id"] + ": " + string.Join(",", issues));
        }
    }
}
