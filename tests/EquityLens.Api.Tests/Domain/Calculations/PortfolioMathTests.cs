namespace EquityLens.Api.Domain.Calculations;

public sealed class PortfolioMathTests
{
    [Theory]
    [InlineData(0, 100, 0)]
    [InlineData(10, 123.45, 1234.50)]
    [InlineData(-5, 50, -250)]
    [InlineData(100, 0.01, 1)]
    public void CalculateMarketValue_ReturnsQuantityTimesPrice(
        decimal quantity, decimal price, decimal expected)
    {
        var result = PortfolioMath.CalculateMarketValue(quantity, price);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(0, 100, 0)]
    [InlineData(10, 100, 1000)]
    [InlineData(-5, 50, -250)]
    [InlineData(100, 0.01, 1)]
    public void CalculateCostValue_ReturnsQuantityTimesAverageCost(
        decimal quantity, decimal averageCost, decimal expected)
    {
        var result = PortfolioMath.CalculateCostValue(quantity, averageCost);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(1234.50, 1000, 234.50)]
    [InlineData(500, 1000, -500)]
    [InlineData(0, 0, 0)]
    [InlineData(-100, -200, 100)]
    public void CalculateUnrealizedPnl_ReturnsMarketMinusCost(
        decimal marketValue, decimal costValue, decimal expected)
    {
        var result = PortfolioMath.CalculateUnrealizedPnl(marketValue, costValue);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(234.50, 1000, 0.2345)]
    [InlineData(-500, 1000, -0.5)]
    [InlineData(0, 100, 0)]
    [InlineData(100, 100, 1)]
    public void CalculateUnrealizedPnlPercent_WhenCostNotZero_ReturnsRatio(
        decimal unrealizedPnl, decimal costValue, decimal expected)
    {
        var result = PortfolioMath.CalculateUnrealizedPnlPercent(unrealizedPnl, costValue);
        Assert.NotNull(result);
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public void CalculateUnrealizedPnlPercent_WhenCostIsZero_ReturnsNull()
    {
        var result = PortfolioMath.CalculateUnrealizedPnlPercent(100, 0);
        Assert.Null(result);
    }

    [Theory]
    [InlineData(250, 1000, 0.25)]
    [InlineData(0, 1000, 0)]
    [InlineData(1000, 1000, 1)]
    [InlineData(500, 250, 2)]
    public void CalculateWeight_WhenTotalNotZero_ReturnsRatio(
        decimal marketValue, decimal totalMarketValue, decimal expected)
    {
        var result = PortfolioMath.CalculateWeight(marketValue, totalMarketValue);
        Assert.NotNull(result);
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public void CalculateWeight_WhenTotalIsZero_ReturnsNull()
    {
        var result = PortfolioMath.CalculateWeight(250, 0);
        Assert.Null(result);
    }
}
