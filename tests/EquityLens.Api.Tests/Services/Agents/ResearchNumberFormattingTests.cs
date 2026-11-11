using System.Text.Json.Nodes;
using EquityLens.Api.Services.Agents.Loop;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class ResearchNumberFormattingTests
{
    [Theory]
    [InlineData("營業毛利 **60 元** ÷ 營業收入 **100 元** ≈ **60%** [1]。", false)]
    [InlineData("營業毛利 __60 元__ ÷ 營業收入 __100 元__ ≈ __60%__ [1]。", false)]
    [InlineData("營業毛利 **60 元** ÷ 營業收入 **100 元** ≈ **80%** [1]。", true)]
    [InlineData("營業毛利 **70 元** ÷ 營業收入 **100 元** ≈ **70%** [1]。", true)]
    [InlineData("營業毛利 **60 元** ÷ 營業收入 **100 元** ≈ **60%**。", true)]
    public void RatioIgnoresPairedMarkdownButStillRequiresExactSourceOperandsAndCitation(string answer, bool blocked)
    {
        var citations = new JsonObject[] { new() { ["citationIndex"] = 1, ["quoteText"] = "營業毛利 60 元，營業收入 100 元" } };
        Assert.Equal(blocked, ResearchNumberTraceability.Check(answer, citations).Blocking);
    }

    [Theory]
    [InlineData("前輪2025年財報尚未核實 [P1]。", false)]
    [InlineData("前輪2024年財報尚未核實 [P1]。", true)]
    [InlineData("金額2025元 [P1]。", true)]
    [InlineData("前輪2025年財報尚未核實。", true)]
    public void PriorResearchYear_IsOnlyACitedTopicIdentifier(string answer, bool blocked)
    {
        var facts = new LoopDataFact[] { new("P1", "research", "prior_research", new() {
            ["question"] = "台積電2025年財報如何", ["status"] = "Unavailable" }) };
        Assert.Equal(blocked, PortfolioQualityGateNodeHandler.Check(new(answer, answer.Contains("[P1]") ? ["P1"] : []), facts).Contains("UntraceableNumbers"));
    }
}
