using System.Text.Json.Nodes;
using EquityLens.Api.Services.Agents.Loop;

namespace EquityLens.Api.Tests.Services.Agents;

/// <summary>實測誤攔：方法參數、情境年份與使用者目標不應讓正確答案被整份拒絕。</summary>
public sealed class PortfolioGateMethodNumberTests
{
    private static readonly LoopDataFact Risk = new("P1", "risk", "run_risk_analysis", new JsonObject
    {
        ["volatility"] = .5673m, ["observationCount"] = 242, ["method"] = "historical; annualization=252",
    });

    private static readonly LoopDataFact Stress = new("P2", "stress", "run_stress_test", new JsonObject
    {
        ["scenarios"] = new JsonArray(new JsonObject { ["name"] = "資料期間最差 20 日", ["from"] = "2008-09-29", ["to"] = "2008-10-28", ["impact"] = -.3114m }),
    });

    [Fact]
    public void Method_parameters_present_in_tool_data_are_traceable()
    {
        var answer = new LoopAnswer("年化波動率為 56.73%，以 242 個共同交易日的日報酬年化（252 日）計算。[P1]", ["P1"]);
        Assert.DoesNotContain("UntraceableNumbers", PortfolioQualityGateNodeHandler.Check(answer, [Risk]));
    }

    [Fact]
    public void Stress_window_and_year_from_cited_scenario_are_traceable()
    {
        var answer = new LoopAnswer("回放最差 20 日，2008 年金融海嘯期間投組衝擊為 -31.14%。[P2]", ["P2"]);
        Assert.DoesNotContain("UntraceableNumbers", PortfolioQualityGateNodeHandler.Check(answer, [Stress]));
    }

    [Fact]
    public void Method_like_integers_missing_from_tool_data_are_still_rejected()
    {
        var rejected = new List<string>();
        var answer = new LoopAnswer("以 300 個共同交易日計算，年化波動率為 56.73%。[P1]", ["P1"]);
        Assert.Contains("UntraceableNumbers", PortfolioQualityGateNodeHandler.Check(answer, [Risk], rejected));
        Assert.Contains("300", rejected);
    }

    [Fact]
    public void User_target_is_allowed_only_when_attributed_to_user()
    {
        const string question = "目標是把年化波動降到 40% 以下";
        var attributed = new LoopAnswer("年化波動率為 56.73%，高於你提出的 40% 目標。[P1]", ["P1"]);
        Assert.DoesNotContain("UntraceableNumbers", PortfolioQualityGateNodeHandler.Check(attributed, [Risk], userQuestion: question));
        var asFact = new LoopAnswer("調整後年化波動率為 40%。[P1]", ["P1"]);
        Assert.Contains("UntraceableNumbers", PortfolioQualityGateNodeHandler.Check(asFact, [Risk], userQuestion: question));
        Assert.Contains("UntraceableNumbers", PortfolioQualityGateNodeHandler.Check(attributed, [Risk]));
    }
}

public sealed class PortfolioGateUserTopicTests
{
    private static readonly LoopDataFact Risk = new("P1", "risk", "run_risk_analysis", new JsonObject { ["volatility"] = .5673m });

    [Fact]
    public void Year_from_user_question_can_be_named_when_explaining_missing_data()
    {
        const string question = "回到剛剛那個高波動的，如果遇到 2008 金融海嘯會跌多少？";
        var answer = new LoopAnswer("目前工具沒有 2008 年金融海嘯的歷史回放，無法估計跌幅。[P1]", ["P1"]);
        Assert.DoesNotContain("UntraceableNumbers", PortfolioQualityGateNodeHandler.Check(answer, [Risk], userQuestion: question));
        Assert.Contains("UntraceableNumbers", PortfolioQualityGateNodeHandler.Check(answer, [Risk]));
        var yearReturn = new LoopAnswer("2008 年報酬為 -45%。[P1]", ["P1"]);
        Assert.Contains("UntraceableNumbers", PortfolioQualityGateNodeHandler.Check(yearReturn, [Risk], userQuestion: question));
    }

    [Fact]
    public void User_target_followed_by_target_word_is_attributed()
    {
        var answer = new LoopAnswer("先前提到的年化波動低於 40% 目標，目前為 56.73%。[P1]", ["P1"]);
        Assert.DoesNotContain("UntraceableNumbers", PortfolioQualityGateNodeHandler.Check(answer, [Risk], userQuestion: "降到 40% 以下"));
    }
}

public sealed class FinMindAssetTypeTests
{
    [Theory]
    [InlineData("0050", "ETF", "ETF")]
    [InlineData("006208", null, "ETF")]
    [InlineData("00679B", "ETF", "ETF")]
    [InlineData("2330", "半導體業", "Equity")]
    public void Etf_metadata_is_not_imported_as_equity(string ticker, string? industry, string expected) =>
        Assert.Equal(expected, EquityLens.Api.Services.MarketData.FinMindMarketDataProvider.InferAssetType(ticker, industry));
}

public sealed class PortfolioGateNameTests
{
    private static LoopDataFact Snapshot(string name) => new("P1", "snapshot", "get_portfolio_snapshot", new JsonObject
    {
        ["diagnosis"] = new JsonObject { ["portfolioName"] = name }, ["weight"] = .2308m,
    });

    [Theory]
    [InlineData("測試1")]
    [InlineData("2025 成長組合")]
    public void Portfolio_names_with_digits_are_not_numeric_claims(string name)
    {
        var answer = new LoopAnswer($"「{name}」直接持有台積電，權重 23.08%。[P1]", ["P1"]);
        Assert.DoesNotContain("UntraceableNumbers", PortfolioQualityGateNodeHandler.Check(answer, [Snapshot(name)]));
    }

    [Fact]
    public void Masking_names_does_not_hide_other_numbers()
    {
        var answer = new LoopAnswer("「測試1」權重 99%。[P1]", ["P1"]);
        Assert.Contains("UntraceableNumbers", PortfolioQualityGateNodeHandler.Check(answer, [Snapshot("測試1")]));
    }
}
