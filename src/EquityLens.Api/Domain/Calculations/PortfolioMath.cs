namespace EquityLens.Api.Domain.Calculations;

public static class PortfolioMath
{
    public static decimal CalculateMarketValue(decimal quantity, decimal latestPrice)
    {
        return quantity * latestPrice;
    }

    public static decimal CalculateCostValue(decimal quantity, decimal averageCost)
    {
        return quantity * averageCost;
    }

    public static decimal CalculateUnrealizedPnl(decimal marketValue, decimal costValue)
    {
        return marketValue - costValue;
    }

    public static decimal? CalculateUnrealizedPnlPercent(decimal unrealizedPnl, decimal costValue)
    {
        return costValue == 0 ? null : unrealizedPnl / costValue;
    }

    public static decimal? CalculateWeight(decimal marketValue, decimal totalMarketValue)
    {
        return totalMarketValue == 0 ? null : marketValue / totalMarketValue;
    }
}
