using EquityLens.Api.Services.MarketData;
namespace EquityLens.Api.Tests.Services.MarketData;
public sealed class TaiwanTradingClockTests
{
    [Theory]
    [InlineData("2026-10-01T09:59:00+00:00", "2026-09-30")]
    [InlineData("2026-10-01T10:00:00+00:00", "2026-10-01")]
    [InlineData("2026-10-01T16:30:00+00:00", "2026-10-01")]
    public void BeforeAndAfterTaipeiClose_ReturnsCompleteDate(string instant, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), TaiwanTradingClock.LatestCompleteDate(DateTimeOffset.Parse(instant)));
}
