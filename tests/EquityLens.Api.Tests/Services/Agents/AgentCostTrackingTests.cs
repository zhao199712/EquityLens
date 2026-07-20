using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class AgentCostTrackingTests
{
    [Fact]
    public void AgentRunNode_CostFields_DefaultToNull()
    {
        var node = new AgentRunNode();

        Assert.Null(node.InputTokens);
        Assert.Null(node.OutputTokens);
        Assert.Null(node.EstimatedCostUsd);
    }

    [Fact]
    public void AgentRun_TotalCostFields_DefaultToZero()
    {
        var run = new AgentRun();

        Assert.Equal(0, run.TotalInputTokens);
        Assert.Equal(0, run.TotalOutputTokens);
        Assert.Equal(0m, run.TotalEstimatedCostUsd);
    }

    [Fact]
    public void AgentRunNode_CanSetCostFields()
    {
        var node = new AgentRunNode
        {
            InputTokens = 1500,
            OutputTokens = 500,
            EstimatedCostUsd = 0.025m
        };

        Assert.Equal(1500, node.InputTokens);
        Assert.Equal(500, node.OutputTokens);
        Assert.Equal(0.025m, node.EstimatedCostUsd);
    }

    [Fact]
    public void AgentRun_CanSetTotalCostFields()
    {
        var run = new AgentRun
        {
            TotalInputTokens = 10000,
            TotalOutputTokens = 3000,
            TotalEstimatedCostUsd = 0.08m
        };

        Assert.Equal(10000, run.TotalInputTokens);
        Assert.Equal(3000, run.TotalOutputTokens);
        Assert.Equal(0.08m, run.TotalEstimatedCostUsd);
    }

    [Fact]
    public void AgentRun_TotalCost_AccumulatesFromNodes()
    {
        var run = new AgentRun();
        var nodes = new[]
        {
            new AgentRunNode { InputTokens = 1000, OutputTokens = 300, EstimatedCostUsd = 0.01m },
            new AgentRunNode { InputTokens = 2000, OutputTokens = 600, EstimatedCostUsd = 0.02m },
            new AgentRunNode { InputTokens = 1500, OutputTokens = 500, EstimatedCostUsd = 0.015m }
        };

        foreach (var node in nodes)
        {
            run.TotalInputTokens += node.InputTokens ?? 0;
            run.TotalOutputTokens += node.OutputTokens ?? 0;
            run.TotalEstimatedCostUsd += node.EstimatedCostUsd ?? 0;
        }

        Assert.Equal(4500, run.TotalInputTokens);
        Assert.Equal(1400, run.TotalOutputTokens);
        Assert.Equal(0.045m, run.TotalEstimatedCostUsd);
    }

    [Fact]
    public void AgentRun_TotalCost_HandlesNullNodeCosts()
    {
        var run = new AgentRun();
        var node = new AgentRunNode(); // all cost fields null

        run.TotalInputTokens += node.InputTokens ?? 0;
        run.TotalOutputTokens += node.OutputTokens ?? 0;
        run.TotalEstimatedCostUsd += node.EstimatedCostUsd ?? 0;

        Assert.Equal(0, run.TotalInputTokens);
        Assert.Equal(0, run.TotalOutputTokens);
        Assert.Equal(0m, run.TotalEstimatedCostUsd);
    }
}
