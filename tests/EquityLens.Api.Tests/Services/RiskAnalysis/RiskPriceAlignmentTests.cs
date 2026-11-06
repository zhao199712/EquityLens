using EquityLens.Api.Contracts.MarketPrices;
using EquityLens.Api.Services.RiskAnalysis;

namespace EquityLens.Api.Tests.Services.RiskAnalysis;

public sealed class RiskPriceAlignmentTests
{
    private static readonly DateOnly From = new(2024, 1, 1);
    private static readonly Guid First = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Second = Guid.Parse("00000000-0000-0000-0000-000000000002");

    [Fact]
    public void Align_SeedsLeadingGapFromLatestEarlierRowWithoutLookahead()
    {
        var result = RiskPriceAlignment.Align(new Dictionary<Guid, IReadOnlyList<MarketPriceResponse>>
        {
            // Deliberately unsorted. The nearest row is day 1, not the first row or future day 4.
            [First] = [Price(First, 4, 400m), Price(First, 0, 10m), Price(First, 5, 500m), Price(First, 1, 11m)],
            [Second] = [Price(Second, 2, 20m), Price(Second, 3, 21m), Price(Second, 5, 23m)]
        }, price => price.AdjustedClose);

        Assert.Equal(new[] { From.AddDays(2), From.AddDays(3), From.AddDays(4), From.AddDays(5) }, result.Dates);
        Assert.Equal(new[] { 11m, 11m, 400m, 500m }, result.Prices[First]);
        Assert.Equal(new[] { 20m, 21m, 21m, 23m }, result.Prices[Second]);
        Assert.Equal(2, result.CarriedForwardCounts[First]);
        Assert.Equal(1, result.CarriedForwardCounts[Second]);
        Assert.False(result.HasUnavailablePrice);
    }

    [Fact]
    public void Align_MissingAdjustedSeedDoesNotUseEarlierValidOrFuturePrices()
    {
        var result = RiskPriceAlignment.Align(new Dictionary<Guid, IReadOnlyList<MarketPriceResponse>>
        {
            [First] = [Price(First, 0, 10m), Price(First, 1, null), Price(First, 4, 14m)],
            [Second] = [Price(Second, 2, 20m), Price(Second, 3, 21m), Price(Second, 4, 22m)]
        }, price => price.AdjustedClose);

        Assert.True(result.HasUnavailablePrice);
        Assert.Equal(new[] { 0m, 0m, 14m }, result.Prices[First]);
        Assert.Equal(2, result.CarriedForwardCounts[First]);
    }

    [Fact]
    public void Align_UsesCallerSelectorForSeed()
    {
        var result = RiskPriceAlignment.Align(new Dictionary<Guid, IReadOnlyList<MarketPriceResponse>>
        {
            [First] = [Price(First, 0, null), Price(First, 2, 12m)],
            [Second] = [Price(Second, 1, 20m), Price(Second, 2, 21m)]
        }, price => price.AdjustedClose ?? price.Close);

        Assert.Equal(new[] { 100m, 12m }, result.Prices[First]);
        Assert.False(result.HasUnavailablePrice);
    }

    [Fact]
    public void Align_StartDateRowTakesPrecedenceOverUnavailableEarlierRow()
    {
        var result = RiskPriceAlignment.Align(new Dictionary<Guid, IReadOnlyList<MarketPriceResponse>>
        {
            [First] = [Price(First, 0, null), Price(First, 1, 11m), Price(First, 2, 12m)],
            [Second] = [Price(Second, 1, 20m), Price(Second, 2, 21m)]
        }, price => price.AdjustedClose);

        Assert.Equal(new[] { 11m, 12m }, result.Prices[First]);
        Assert.Equal(0, result.CarriedForwardCounts[First]);
        Assert.False(result.HasUnavailablePrice);
    }

    [Fact]
    public void Align_DoesNotExtendBeyondCommonEnd()
    {
        var result = RiskPriceAlignment.Align(new Dictionary<Guid, IReadOnlyList<MarketPriceResponse>>
        {
            [First] = [Price(First, 0, 10m), Price(First, 3, 13m), Price(First, 5, 15m)],
            [Second] = [Price(Second, 1, 20m), Price(Second, 2, 21m)]
        }, price => price.AdjustedClose);

        Assert.Equal(new[] { From.AddDays(1), From.AddDays(2) }, result.Dates);
        Assert.Equal(new[] { 10m, 10m }, result.Prices[First]);
        Assert.Equal(2, result.CarriedForwardCounts[First]);
    }

    [Fact]
    public void Align_NonOverlappingHistoriesRemainEmpty()
    {
        var result = RiskPriceAlignment.Align(new Dictionary<Guid, IReadOnlyList<MarketPriceResponse>>
        {
            [First] = [Price(First, 0, 10m)],
            [Second] = [Price(Second, 1, 20m)]
        }, price => price.AdjustedClose);

        Assert.Empty(result.Dates);
        Assert.All(result.Prices.Values, Assert.Empty);
        Assert.All(result.CarriedForwardCounts.Values, count => Assert.Equal(0, count));
        Assert.False(result.HasUnavailablePrice);
    }

    private static MarketPriceResponse Price(Guid securityId, int day, decimal? adjustedClose) => new(
        Guid.NewGuid(), securityId, From.AddDays(day).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        "1d", 100m, 100m, 100m, 100m, adjustedClose, null, "fixture");
}
