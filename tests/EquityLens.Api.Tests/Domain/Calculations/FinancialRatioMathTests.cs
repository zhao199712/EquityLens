namespace EquityLens.Api.Domain.Calculations;

public sealed class FinancialRatioMathTests
{
    [Theory]
    [InlineData(40, 100, 0.40)]
    [InlineData(0, 100, 0)]
    [InlineData(-10, 100, -0.10)]
    public void CalculateGrossMargin_ReturnsRatio(
        decimal grossProfit, decimal revenue, decimal expected)
    {
        var result = FinancialRatioMath.CalculateGrossMargin(grossProfit, revenue);
        Assert.NotNull(result);
        Assert.Equal(expected, result!.Value);
    }

    [Fact]
    public void CalculateGrossMargin_RevenueZero_ReturnsNull()
    {
        var result = FinancialRatioMath.CalculateGrossMargin(40, 0);
        Assert.Null(result);
    }

    [Theory]
    [InlineData(20, 100, 0.20)]
    [InlineData(0, 100, 0)]
    public void CalculateOperatingMargin_ReturnsRatio(
        decimal operatingIncome, decimal revenue, decimal expected)
    {
        var result = FinancialRatioMath.CalculateOperatingMargin(operatingIncome, revenue);
        Assert.NotNull(result);
        Assert.Equal(expected, result!.Value);
    }

    [Fact]
    public void CalculateOperatingMargin_RevenueZero_ReturnsNull()
    {
        var result = FinancialRatioMath.CalculateOperatingMargin(20, 0);
        Assert.Null(result);
    }

    [Theory]
    [InlineData(15, 100, 0.15)]
    [InlineData(-5, 100, -0.05)]
    public void CalculateNetMargin_ReturnsRatio(
        decimal netIncome, decimal revenue, decimal expected)
    {
        var result = FinancialRatioMath.CalculateNetMargin(netIncome, revenue);
        Assert.NotNull(result);
        Assert.Equal(expected, result!.Value);
    }

    [Fact]
    public void CalculateRoe_ReturnsRatio()
    {
        var result = FinancialRatioMath.CalculateRoe(100, 500);
        Assert.NotNull(result);
        Assert.Equal(0.20m, result!.Value);
    }

    [Fact]
    public void CalculateRoe_EquityZero_ReturnsNull()
    {
        var result = FinancialRatioMath.CalculateRoe(100, 0);
        Assert.Null(result);
    }

    [Fact]
    public void CalculateRoa_ReturnsRatio()
    {
        var result = FinancialRatioMath.CalculateRoa(80, 1000);
        Assert.NotNull(result);
        Assert.Equal(0.08m, result!.Value);
    }

    [Fact]
    public void CalculateRoa_AssetsZero_ReturnsNull()
    {
        var result = FinancialRatioMath.CalculateRoa(80, 0);
        Assert.Null(result);
    }

    [Fact]
    public void CalculateDebtToEquity_ReturnsRatio()
    {
        var result = FinancialRatioMath.CalculateDebtToEquity(300, 500);
        Assert.NotNull(result);
        Assert.Equal(0.60m, result!.Value);
    }

    [Fact]
    public void CalculateDebtToEquity_EquityZero_ReturnsNull()
    {
        var result = FinancialRatioMath.CalculateDebtToEquity(300, 0);
        Assert.Null(result);
    }

    [Fact]
    public void CalculateCurrentRatio_ReturnsRatio()
    {
        var result = FinancialRatioMath.CalculateCurrentRatio(200, 100);
        Assert.NotNull(result);
        Assert.Equal(2m, result!.Value);
    }

    [Fact]
    public void CalculateCurrentRatio_LiabilitiesZero_ReturnsNull()
    {
        var result = FinancialRatioMath.CalculateCurrentRatio(200, 0);
        Assert.Null(result);
    }

    [Fact]
    public void CalculateQuickRatio_ExcludesInventory()
    {
        var result = FinancialRatioMath.CalculateQuickRatio(200, 50, 100);
        Assert.NotNull(result);
        Assert.Equal(1.5m, result!.Value);
    }

    [Fact]
    public void CalculateQuickRatio_LiabilitiesZero_ReturnsNull()
    {
        var result = FinancialRatioMath.CalculateQuickRatio(200, 50, 0);
        Assert.Null(result);
    }

    [Theory]
    [InlineData(120, 100, 0.20)]
    [InlineData(80, 100, -0.20)]
    [InlineData(100, 100, 0)]
    public void CalculateRevenueGrowth_ReturnsRatio(
        decimal current, decimal previous, decimal expected)
    {
        var result = FinancialRatioMath.CalculateRevenueGrowth(current, previous);
        Assert.NotNull(result);
        Assert.Equal(expected, result!.Value);
    }

    [Fact]
    public void CalculateRevenueGrowth_PreviousZero_ReturnsNull()
    {
        var result = FinancialRatioMath.CalculateRevenueGrowth(120, 0);
        Assert.Null(result);
    }

    [Theory]
    [InlineData(5.5, 5.0, 0.10)]
    public void CalculateEpsGrowth_ReturnsRatio(
        decimal current, decimal previous, decimal expected)
    {
        var result = FinancialRatioMath.CalculateEpsGrowth(current, previous);
        Assert.NotNull(result);
        Assert.Equal(expected, result!.Value);
    }

    [Fact]
    public void CalculateEpsGrowth_PreviousZero_ReturnsNull()
    {
        var result = FinancialRatioMath.CalculateEpsGrowth(5, 0);
        Assert.Null(result);
    }

    [Fact]
    public void CalculatePeRatio_ReturnsRatio()
    {
        var result = FinancialRatioMath.CalculatePeRatio(150, 10);
        Assert.NotNull(result);
        Assert.Equal(15m, result!.Value);
    }

    [Theory]
    [InlineData(150, 0)]
    [InlineData(150, -5)]
    public void CalculatePeRatio_NonPositiveEps_ReturnsNull(
        decimal price, decimal eps)
    {
        var result = FinancialRatioMath.CalculatePeRatio(price, eps);
        Assert.Null(result);
    }

    [Fact]
    public void CalculatePbRatio_ReturnsRatio()
    {
        var result = FinancialRatioMath.CalculatePbRatio(100, 50);
        Assert.NotNull(result);
        Assert.Equal(2m, result!.Value);
    }

    [Fact]
    public void CalculatePbRatio_NonPositiveBookValue_ReturnsNull()
    {
        var result = FinancialRatioMath.CalculatePbRatio(100, 0);
        Assert.Null(result);
    }

    [Fact]
    public void CalculatePsRatio_ReturnsRatio()
    {
        var result = FinancialRatioMath.CalculatePsRatio(500, 200);
        Assert.NotNull(result);
        Assert.Equal(2.5m, result!.Value);
    }

    [Fact]
    public void CalculatePsRatio_NonPositiveRevenue_ReturnsNull()
    {
        var result = FinancialRatioMath.CalculatePsRatio(500, 0);
        Assert.Null(result);
    }

    [Fact]
    public void CalculateEvEbitda_ReturnsRatio()
    {
        var result = FinancialRatioMath.CalculateEvEbitda(1000, 100);
        Assert.NotNull(result);
        Assert.Equal(10m, result!.Value);
    }

    [Fact]
    public void CalculateEvEbitda_NonPositiveEbitda_ReturnsNull()
    {
        var result = FinancialRatioMath.CalculateEvEbitda(1000, -10);
        Assert.Null(result);
    }

    [Fact]
    public void CalculateDividendYield_ReturnsRatio()
    {
        var result = FinancialRatioMath.CalculateDividendYield(4, 100);
        Assert.NotNull(result);
        Assert.Equal(0.04m, result!.Value);
    }

    [Fact]
    public void CalculateDividendYield_PriceZero_ReturnsNull()
    {
        var result = FinancialRatioMath.CalculateDividendYield(4, 0);
        Assert.Null(result);
    }
}
