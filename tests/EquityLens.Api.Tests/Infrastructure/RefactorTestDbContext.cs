using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Tests.Infrastructure;

internal sealed class RefactorTestDbContext(DbContextOptions<EquityLensDbContext> options) : EquityLensDbContext(options)
{
    public static RefactorTestDbContext Create() => new(new DbContextOptionsBuilder<EquityLensDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Ignore<DocumentEmbedding>();
        modelBuilder.Entity<AgentRun>().Property(x => x.Id).ValueGeneratedNever();
        modelBuilder.Entity<AgentRunNode>().Property(x => x.Id).ValueGeneratedNever();
        modelBuilder.Entity<AgentRunEvent>().Property(x => x.Id).ValueGeneratedNever();
        modelBuilder.Entity<AgentToolCall>().Property(x => x.Id).ValueGeneratedNever();
        modelBuilder.Entity<AgentRunWakeOutbox>().Property(x => x.Id).ValueGeneratedNever();
    }
}
