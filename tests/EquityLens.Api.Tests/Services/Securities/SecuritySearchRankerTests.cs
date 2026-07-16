using EquityLens.Api.Services.Securities;

namespace EquityLens.Api.Tests.Services.Securities;

public sealed class SecuritySearchRankerTests
{
    [Fact]
    public void Rank_PrioritizesEquityAndEtf_AndExcludesWarrants()
    {
        var results = SecuritySearchRanker.Rank(
            [
                Candidate("03001", "富邦金元大購01", "Equity", "FinMind"),
                Candidate("2881", "富邦金", "Equity", "Local", Guid.NewGuid()),
                Candidate("006208", "富邦台50", "ETF", "FinMind"),
                Candidate("B001", "富邦金公司債", "Bond", "FinMind")
            ],
            "富邦");

        Assert.Collection(results,
            result => Assert.Equal("2881", result.Ticker),
            result => Assert.Equal("006208", result.Ticker),
            result => Assert.Equal("B001", result.Ticker));
    }

    [Fact]
    public void Rank_TreatsTaiwanBondEtfTickerAsEtf_WhenProviderLabelsItEquity()
    {
        var results = SecuritySearchRanker.Rank(
            [
                Candidate("00695B", "富邦投等債", "Equity", "FinMind"),
                Candidate("2881", "富邦金", "Equity", "FinMind")
            ],
            "富邦");

        Assert.Equal(["2881", "00695B"], results.Select(result => result.Ticker));
    }

    [Fact]
    public void Rank_DeduplicatesByExchangeAndTicker_PreferringLocalResult()
    {
        var localId = Guid.NewGuid();
        var results = SecuritySearchRanker.Rank(
            [
                Candidate("2881", "富邦金融", "Equity", "FinMind"),
                Candidate("2881", "富邦金", "Equity", "Local", localId)
            ],
            "富邦");

        var result = Assert.Single(results);
        Assert.Equal(localId, result.SecurityId);
        Assert.Equal("富邦金", result.Name);
    }

    [Fact]
    public void Rank_UsesExactThenPrefixThenContainsMatching()
    {
        var results = SecuritySearchRanker.Rank(
            [
                Candidate("2881", "台灣富邦金", "Equity", "FinMind"),
                Candidate("9999", "富邦金控", "Equity", "FinMind"),
                Candidate("7777", "富邦", "Equity", "FinMind")
            ],
            "富邦");

        Assert.Equal(["7777", "9999", "2881"], results.Select(result => result.Ticker));
    }

    [Fact]
    public void CountEligible_ExcludesWarrantBeforeFallbackDecision()
    {
        var candidates = Enumerable.Range(0, 10)
            .Select(index => Candidate($"0{index:0000}", $"富邦購{index}", "Equity", "Local"));

        Assert.Equal(0, SecuritySearchRanker.CountEligible(candidates));
    }

    private static SecuritySearchCandidate Candidate(string ticker, string name, string assetType, string source, Guid? securityId = null)
        => new(securityId, ticker, "TWSE", name, assetType, "TWD", null, null, null, source);
}
