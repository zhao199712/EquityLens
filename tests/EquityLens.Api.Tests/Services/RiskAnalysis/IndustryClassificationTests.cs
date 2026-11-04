using System.Text.Json;
using EquityLens.Api.Contracts.PortfolioHoldings;
using EquityLens.Api.Services.RiskAnalysis;

namespace EquityLens.Api.Tests.Services.RiskAnalysis;

public sealed class IndustryClassificationTests
{
    [Theory]
    [InlineData(" 半導體業 ", null, "電子工業")]
    [InlineData("電子零組件業", null, "電子工業")]
    [InlineData(null, "金融保險", "金融保險業")]
    [InlineData("自訂未知產業", "電子工業", "自訂未知產業")]
    [InlineData(null, null, "未分類")]
    public void Normalization_KeepsUnknownAndOriginalClassification(string? raw, string? sector, string expected)
    {
        Assert.Equal(expected, EquityLens.Api.Domain.Calculations.IndustryClassification.Normalize(raw, sector));
        Assert.Equal(raw ?? sector, EquityLens.Api.Domain.Calculations.IndustryClassification.Raw(raw, sector));
    }

    [Fact]
    public void BroadAndDetailedElectronicsUseTheSameConcentrationGroup()
    {
        PortfolioHoldingResponse Holding(string industry) => JsonSerializer.Deserialize<PortfolioHoldingResponse>(
            JsonSerializer.Serialize(new { industry }), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(PortfolioRiskModelCalculator.ResolveIndustry(Holding("電子工業")),
            PortfolioRiskModelCalculator.ResolveIndustry(Holding("電子零組件業")));
    }
}
