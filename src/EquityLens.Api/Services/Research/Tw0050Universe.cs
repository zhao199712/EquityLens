namespace EquityLens.Api.Services.Research;

public static class Tw0050Universe
{
    private static readonly HashSet<string> Tickers = new(StringComparer.OrdinalIgnoreCase)
    {
        "1101", "1216", "1301", "1303", "1326", "1590", "2002", "2207", "2303", "2308",
        "2317", "2327", "2330", "2357", "2379", "2382", "2395", "2408", "2412", "2454",
        "2603", "2609", "2615", "2880", "2881", "2882", "2883", "2884", "2885", "2886",
        "2887", "2890", "2891", "2892", "2912", "3008", "3034", "3045", "3231", "3661",
        "3711", "4904", "4938", "5871", "5876", "5880", "6505", "6669", "6691", "8046"
    };

    public static bool Contains(string ticker)
    {
        return Tickers.Contains(Normalize(ticker));
    }

    public static IReadOnlyCollection<string> AllTickers => Tickers;

    private static string Normalize(string ticker)
    {
        return ticker.Trim().ToUpperInvariant();
    }
}
