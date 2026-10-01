namespace EquityLens.Api.Services.MarketData;

public static class TaiwanTradingClock
{
    public static DateOnly LatestCompleteDate(DateTimeOffset utcNow)
    {
        var local = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(utcNow, "Asia/Taipei");
        var day = DateOnly.FromDateTime(local.DateTime);
        return local.Hour >= 18 ? day : day.AddDays(-1);
    }
}
