using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pgvector.EntityFrameworkCore;

namespace EquityLens.Api.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<EquityLensDbContext>
{
    public EquityLensDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<EquityLensDbContext>()
            .UseNpgsql(
                "Host=localhost:5433;Database=equitylens;Username=equitylens;Password=equitylens",
                o => o.UseVector())
            .Options;
        return new EquityLensDbContext(options);
    }
}
