using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.PortfolioTransactions;

namespace EquityLens.Api.Tests.Services.PortfolioTransactions;

public sealed class FifoPortfolioCalculatorTests
{
    private static readonly Guid SecurityId = Guid.NewGuid();

    [Fact]
    public void Calculate_BuyThenFullSell_MatchesCostAndRealizedPnl()
    {
        var created = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var transactions = new[]
        {
            Buy(100m, 10m, 0m, createdAt: created),
            Sell(100m, 12m, 10m, createdAt: created.AddSeconds(1)),
        };

        var result = FifoPortfolioCalculator.Calculate(transactions);

        Assert.True(result.IsValid);
        Assert.Empty(result.LotsBySecurity[SecurityId]);
        var sale = Assert.Single(result.Sales.Values);
        Assert.Equal(100m * 12m - 10m, sale.NetProceeds);
        Assert.Equal(100m * 10m, sale.MatchedCost);
        Assert.Equal(1190m - 1000m, sale.RealizedPnl);
        Assert.Equal(190m, result.TotalRealizedPnl);
    }

    [Fact]
    public void Calculate_PartialSell_KeepsRemainingLot()
    {
        var created = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var transactions = new[]
        {
            Buy(100m, 10m, 100m, createdAt: created),
            Sell(30m, 12m, 30m, createdAt: created.AddSeconds(1)),
        };

        var result = FifoPortfolioCalculator.Calculate(transactions);

        Assert.True(result.IsValid);
        var lot = Assert.Single(result.LotsBySecurity[SecurityId]);
        Assert.Equal(70m, lot.Quantity);
        Assert.Equal(770m, lot.CostValue); // (1000 + 100) * 70 / 100
        var sale = Assert.Single(result.Sales.Values);
        Assert.Equal(30m * 12m - 30m, sale.NetProceeds);
        Assert.Equal(1100m * 30m / 100m, sale.MatchedCost);
    }

    [Fact]
    public void Calculate_MultipleBuysThenSell_UsesFifoOrder()
    {
        var created = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var transactions = new[]
        {
            Buy(50m, 10m, 0m, createdAt: created),
            Buy(50m, 12m, 0m, createdAt: created.AddSeconds(1)),
            Sell(60m, 15m, 0m, createdAt: created.AddSeconds(2)),
        };

        var result = FifoPortfolioCalculator.Calculate(transactions);

        Assert.True(result.IsValid);
        var lot = Assert.Single(result.LotsBySecurity[SecurityId]);
        Assert.Equal(40m, lot.Quantity);
        Assert.Equal(480m, lot.CostValue); // remaining from second buy: 40 * 12
        var sale = Assert.Single(result.Sales.Values);
        Assert.Equal(50m * 10m + 10m * 12m, sale.MatchedCost);
        Assert.Equal(60m * 15m - sale.MatchedCost, sale.RealizedPnl);
    }

    [Fact]
    public void Calculate_SellMoreThanAvailable_ReturnsInvalid()
    {
        var transactions = new[]
        {
            Buy(100m, 10m, 0m),
            Sell(101m, 12m, 0m),
        };

        var result = FifoPortfolioCalculator.Calculate(transactions);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
        Assert.Empty(result.Sales);
    }

    [Fact]
    public void Calculate_OutOfChronologicalOrder_SortsByDate()
    {
        var earlierBuy = Buy(50m, 10m, 0m, new DateOnly(2025, 1, 1));
        var laterBuy = Buy(50m, 12m, 0m, new DateOnly(2025, 1, 2));
        var sell = Sell(50m, 15m, 0m, new DateOnly(2025, 1, 2));
        // Intentionally reverse order.
        var transactions = new[] { laterBuy, sell, earlierBuy };

        var result = FifoPortfolioCalculator.Calculate(transactions);

        Assert.True(result.IsValid);
        var sale = Assert.Single(result.Sales.Values);
        Assert.Equal(50m * 10m, sale.MatchedCost); // earliest buy first
    }

    [Fact]
    public void Calculate_SellWithoutAnyBuy_ReturnsInvalid()
    {
        var transactions = new[] { Sell(10m, 12m, 0m) };

        var result = FifoPortfolioCalculator.Calculate(transactions);

        Assert.False(result.IsValid);
    }

    private static PortfolioTransaction Buy(decimal quantity, decimal price, decimal fee, DateOnly? date = null, DateTime? createdAt = null)
        => CreateTransaction("BUY", quantity, price, fee, date ?? DateOnly.FromDateTime(DateTime.UtcNow), createdAt);

    private static PortfolioTransaction Sell(decimal quantity, decimal price, decimal fee, DateOnly? date = null, DateTime? createdAt = null)
        => CreateTransaction("SELL", quantity, price, fee, date ?? DateOnly.FromDateTime(DateTime.UtcNow), createdAt);

    private static PortfolioTransaction CreateTransaction(string type, decimal quantity, decimal price, decimal fee, DateOnly date, DateTime? createdAt = null)
    {
        return new PortfolioTransaction
        {
            Id = Guid.NewGuid(),
            PortfolioId = Guid.NewGuid(),
            SecurityId = SecurityId,
            TransactionType = type,
            Quantity = quantity,
            Price = price,
            Fee = fee,
            TransactionDate = date,
            CreatedAtUtc = createdAt ?? DateTime.UtcNow,
        };
    }
}
