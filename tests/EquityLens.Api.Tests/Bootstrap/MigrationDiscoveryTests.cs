using EquityLens.Api.Data;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

namespace EquityLens.Api.Tests.Bootstrap;

public class MigrationDiscoveryTests
{
    [Fact]
    public void InitialPortfolioFundingIsDiscoverableBetweenLedgerAndDerivedCashFlowMigrations()
    {
        // Metadata-only: no database connection is opened.
        using var db = new EquityLensDbContext(new DbContextOptionsBuilder<EquityLensDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused", options => options.UseVector())
            .Options);
        var migrations = db.Database.GetMigrations().ToList();
        const string funding = "20260711110000_AddInitialPortfolioFunding";
        Assert.Contains(funding, migrations);
        Assert.True(migrations.IndexOf("20260711045727_AddPortfolioCashFlowsAndDividends") < migrations.IndexOf(funding));
        Assert.True(migrations.IndexOf(funding) < migrations.IndexOf("20260712121333_AddPortfolioCashFlowIsSystemDerived"));
    }
}
