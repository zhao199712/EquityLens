using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.MarketData;
using EquityLens.Api.Services.PortfolioValuations;
using EquityLens.Api.Tests.Infrastructure;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Tests.Services.PortfolioValuations;

public sealed class SeedBenchmarkTests
{
    [Fact]
    public async Task CachedOnly_ReturnsImportedPointsWithoutHttp()
    {
        using var db = RefactorTestDbContext.Create();
        var date = new DateOnly(2026, 11, 2);
        db.TaiwanTotalReturnIndices.Add(new TaiwanTotalReturnIndex { TradingDate = date, Value = 12345 });
        await db.SaveChangesAsync();
        using var http = new HttpClient(new RejectHttp());
        var service = new FinMindPortfolioBenchmarkService(http, Options.Create(new FinMindOptions { CachedOnly = true }), db);
        var values = await service.GetTotalReturnIndexAsync(date, date, default);
        Assert.Equal(12345, values[date]);
        Assert.Empty(await service.GetTotalReturnIndexAsync(date.AddYears(1), date.AddYears(1), default));
    }

    private sealed class RejectHttp : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Seed benchmark must not access the network");
    }
}
