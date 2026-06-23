using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pgvector.EntityFrameworkCore;

namespace EquityLens.Api.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<EquityLensDbContext>
{
    public EquityLensDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PostgreSQL")
            ?? $"Host=localhost:{Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5432"};Database={Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "equitylens"};Username={Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "equitylens"};Password={Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? "equitylens_dev_password"}";

        var options = new DbContextOptionsBuilder<EquityLensDbContext>()
            .UseNpgsql(
                connectionString,
                o => o.UseVector())
            .Options;
        return new EquityLensDbContext(options);
    }
}
