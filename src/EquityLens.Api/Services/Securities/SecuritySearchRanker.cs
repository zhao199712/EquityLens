using EquityLens.Api.Contracts.Securities;
using EquityLens.Api.Services.MarketData;

namespace EquityLens.Api.Services.Securities;

/// <summary>
/// Normalizes candidates from local and external sources, then applies the
/// product-level search order independently of provider-specific relevance scores.
/// </summary>
public static class SecuritySearchRanker
{
    public static int CountEligible(IEnumerable<SecuritySearchCandidate> candidates)
        => candidates.Count(candidate => !IsWarrant(candidate));

    public static IReadOnlyList<SecuritySearchCandidate> Rank(
        IEnumerable<SecuritySearchCandidate> candidates,
        string query)
    {
        var normalizedQuery = Normalize(query);

        return candidates
            .Where(candidate => !IsWarrant(candidate))
            .GroupBy(candidate => new
            {
                Exchange = Normalize(candidate.Exchange),
                Ticker = Normalize(candidate.Ticker)
            })
            .Select(group => group
                .OrderByDescending(candidate => candidate.IsLocal)
                .ThenByDescending(candidate => GetTextScore(candidate, normalizedQuery))
                .First())
            .Select(candidate => new { Candidate = candidate, Rank = BuildRank(candidate, normalizedQuery) })
            .OrderBy(item => item.Rank.MatchTier)
            .ThenBy(item => item.Rank.TypePriority)
            .ThenByDescending(item => item.Rank.TextScore)
            .ThenBy(item => item.Candidate.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Candidate.Ticker, StringComparer.Ordinal)
            .Select(item => item.Candidate)
            .ToList();
    }

    private static SearchRank BuildRank(SecuritySearchCandidate candidate, string query)
        => new(GetMatchTier(candidate, query), GetTypePriority(candidate), GetTextScore(candidate, query));

    private static int GetMatchTier(SecuritySearchCandidate candidate, string query)
    {
        var ticker = Normalize(candidate.Ticker);
        var name = Normalize(candidate.Name);

        if (ticker == query) return 0;
        if (name == query) return 1;
        if (name.StartsWith(query, StringComparison.Ordinal)) return 2;
        if (ticker.StartsWith(query, StringComparison.Ordinal)) return 3;
        if (name.Contains(query, StringComparison.Ordinal) || ticker.Contains(query, StringComparison.Ordinal)) return 4;
        return 9;
    }

    private static int GetTypePriority(SecuritySearchCandidate candidate)
    {
        if (IsTaiwanBondEtf(candidate.Ticker)) return 2;

        var type = Normalize(candidate.AssetType);
        if (type.Contains("PREFERRED", StringComparison.Ordinal)) return 1;
        if (type is "EQUITY" or "STOCK" or "COMMONSTOCK" or "COMMON STOCK" || type.Contains("股票", StringComparison.Ordinal)) return 0;
        if (type.Contains("ETF", StringComparison.Ordinal)) return 2;
        if (type.Contains("FUND", StringComparison.Ordinal)) return 3;
        if (type.Contains("DEPOSITARY", StringComparison.Ordinal) || type == "DR") return 4;
        if (type.Contains("CONVERTIBLE", StringComparison.Ordinal)) return 5;
        if (type.Contains("BOND", StringComparison.Ordinal) || type.Contains("債", StringComparison.Ordinal)) return 6;
        if (type.Contains("FUTURE", StringComparison.Ordinal) || type.Contains("期貨", StringComparison.Ordinal)) return 7;
        if (type.Contains("OPTION", StringComparison.Ordinal) || type.Contains("選擇權", StringComparison.Ordinal)) return 8;
        return 99;
    }

    private static bool IsTaiwanBondEtf(string ticker)
    {
        var normalizedTicker = Normalize(ticker);
        return normalizedTicker.Length == 6 && normalizedTicker.EndsWith('B') && normalizedTicker[..5].All(char.IsDigit);
    }

    private static double GetTextScore(SecuritySearchCandidate candidate, string query)
    {
        var tier = GetMatchTier(candidate, query);
        return tier switch
        {
            0 => 100,
            1 => 90,
            2 => 70,
            3 => 60,
            4 => 50,
            _ => 0
        };
    }

    private static bool IsWarrant(SecuritySearchCandidate candidate)
    {
        var type = Normalize(candidate.AssetType);
        if (type.Contains("WARRANT", StringComparison.Ordinal) || type.Contains("權證", StringComparison.Ordinal)) return true;

        // FinMind's TaiwanStockInfo labels all returned instruments as Equity.
        // Taiwanese warrant names conventionally include 購 or 售 and use longer codes.
        return candidate.Ticker.Trim().Length > 4 &&
            (candidate.Name.Contains("購", StringComparison.Ordinal) || candidate.Name.Contains("售", StringComparison.Ordinal));
    }

    private static string Normalize(string? value) => value?.Trim().ToUpperInvariant() ?? string.Empty;

    private sealed record SearchRank(int MatchTier, int TypePriority, double TextScore);
}

public sealed record SecuritySearchCandidate(
    Guid? SecurityId,
    string Ticker,
    string Exchange,
    string Name,
    string? AssetType,
    string Currency,
    string? Isin,
    string? Sector,
    string? Industry,
    string Source)
{
    public bool IsLocal => SecurityId.HasValue || Source.Equals("Local", StringComparison.OrdinalIgnoreCase);

    public static SecuritySearchCandidate FromLocal(SecurityResponse result)
        => new(result.Id, result.Ticker, result.Exchange, result.Name, result.AssetType, result.Currency, result.Isin, result.Sector, result.Industry, "Local");

    public static SecuritySearchCandidate FromExternal(ExternalSecuritySearchResult result)
        => new(null, result.Ticker, result.Exchange, result.Name, result.AssetType, result.Currency, result.Isin, result.Sector, result.Industry, result.Source);

    public SecuritySearchResult ToResponse()
        => new(SecurityId, Ticker, Exchange, Name, AssetType, Currency, Isin, Sector, Industry, Source);
}
