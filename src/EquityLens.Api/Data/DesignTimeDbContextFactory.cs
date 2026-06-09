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
                "Host=localhost:5432;Database=equitylens;Username=ymsh20220;Password=ymsh20220",
                o => o.UseVector())
            .Options;
        return new EquityLensDbContext(options);
    }
}
