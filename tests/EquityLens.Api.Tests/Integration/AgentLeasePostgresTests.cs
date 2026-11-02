using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pgvector.EntityFrameworkCore;

namespace EquityLens.Api.Tests.Integration;

[Collection(PortfolioTransactionPostgresCollection.Name)]
public sealed class AgentLeasePostgresTests(PostgresTransactionDatabaseFixture fixture)
{
    private EquityLensDbContext CreateDb() => new(new DbContextOptionsBuilder<EquityLensDbContext>()
        .UseNpgsql(fixture.ConnectionString!, x => x.UseVector()).Options);

    [PostgresFact]
    public async Task LostLease_RejectsNodeAndToolWritesAtomically()
    {
        await using var old = CreateDb();
        var run = await SeedAsync(old);
        old.AgentWriteFence = new(run.Id, "old", 1, TimeProvider.System);
        await using (var next = CreateDb())
        {
            var claimed = await next.AgentRuns.SingleAsync(x => x.Id == run.Id);
            claimed.LeaseOwner = "next";
            claimed.LeaseVersion++;
            await next.SaveChangesAsync();
        }
        run.Nodes.Single().OutputJson = "{\"stale\":true}";
        old.AgentToolCalls.Add(new AgentToolCall { Id = Guid.NewGuid(), AgentRunId = run.Id,
            ToolName = "stale-tool", Status = "Succeeded", ArgumentsJson = "{}", StartedAtUtc = DateTime.UtcNow });
        await Assert.ThrowsAsync<AgentRunLeaseLostException>(() => old.SaveChangesAsync());
        await using var verify = CreateDb();
        Assert.Null((await verify.AgentRunNodes.SingleAsync(x => x.AgentRunId == run.Id)).OutputJson);
        Assert.Empty(await verify.AgentToolCalls.Where(x => x.AgentRunId == run.Id).ToListAsync());
    }

    [PostgresFact]
    public async Task ExpiredOwner_CannotCommitOrRenew()
    {
        await using var db = CreateDb();
        var run = await SeedAsync(db);
        run.LeaseExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();
        db.AgentWriteFence = new(run.Id, "old", 1, TimeProvider.System);
        run.Nodes.Single().OutputJson = "{}";
        await Assert.ThrowsAsync<AgentRunLeaseLostException>(() => db.SaveChangesAsync());
        using var services = Services();
        var heartbeat = new AgentRunLeaseHeartbeat(services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);
        Assert.False(await heartbeat.RenewAsync(run.Id, "old", 1, CancellationToken.None));
    }

    [PostgresFact]
    public async Task Heartbeat_UsesIndependentContext_AndDoesNotInvalidateResultCommit()
    {
        await using var db = CreateDb();
        var run = await SeedAsync(db);
        var before = run.LeaseExpiresAtUtc;
        db.AgentWriteFence = new(run.Id, "old", 1, TimeProvider.System);
        using var services = Services();
        var heartbeat = new AgentRunLeaseHeartbeat(services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);
        Assert.True(await heartbeat.RenewAsync(run.Id, "old", 1, CancellationToken.None));
        Assert.False(await heartbeat.RenewAsync(run.Id, "other", 1, CancellationToken.None));
        Assert.False(await heartbeat.RenewAsync(run.Id, "old", 0, CancellationToken.None));
        run.Nodes.Single().OutputJson = "{\"valid\":true}";
        await db.SaveChangesAsync();
        await using var verify = CreateDb();
        var stored = await verify.AgentRuns.SingleAsync(x => x.Id == run.Id);
        Assert.True(stored.LeaseExpiresAtUtc > before);
        Assert.True(System.Text.Json.JsonDocument.Parse((await verify.AgentRunNodes.SingleAsync(x => x.AgentRunId == run.Id)).OutputJson!).RootElement.GetProperty("valid").GetBoolean());
    }

    [PostgresFact]
    public async Task ConcurrentClaims_OnlyOneLeaseVersionWins()
    {
        await using var first = CreateDb();
        var run = await SeedAsync(first);
        await using var second = CreateDb();
        var rival = await second.AgentRuns.SingleAsync(x => x.Id == run.Id);
        run.LeaseVersion++; run.LeaseOwner = "first";
        rival.LeaseVersion++; rival.LeaseOwner = "second";
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [PostgresFact]
    public async Task LongExecution_RenewalsKeepLeaseValidBeyondOriginalExpiry()
    {
        await using var db = CreateDb();
        var run = await SeedAsync(db);
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        using var services = Services();
        var heartbeat = new AgentRunLeaseHeartbeat(services.GetRequiredService<IServiceScopeFactory>(), clock);
        Assert.True(await heartbeat.RenewAsync(run.Id, "old", 1, CancellationToken.None));
        clock.Now += TimeSpan.FromMinutes(4);
        Assert.True(await heartbeat.RenewAsync(run.Id, "old", 1, CancellationToken.None));
        clock.Now += TimeSpan.FromMinutes(4);
        Assert.True(await heartbeat.RenewAsync(run.Id, "old", 1, CancellationToken.None));
        db.AgentWriteFence = new(run.Id, "old", 1, clock);
        run.Nodes.Single().OutputJson = "{}";
        await db.SaveChangesAsync();
    }

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private ServiceProvider Services() => new ServiceCollection()
        .AddScoped(_ => CreateDb()).BuildServiceProvider();

    private static async Task<AgentRun> SeedAsync(EquityLensDbContext db)
    {
        var user = new AppUser { Id = Guid.NewGuid(), Email = $"lease-{Guid.NewGuid():N}@test.invalid", DisplayName = "Lease test" };
        var run = new AgentRun { Id = Guid.NewGuid(), UserId = user.Id, WorkflowType = "LeaseTest", AgentType = "Research",
            Status = "Running", LeaseOwner = "old", LeaseVersion = 1, LeaseExpiresAtUtc = DateTime.UtcNow.AddMinutes(1) };
        run.Nodes.Add(new AgentRunNode { Id = Guid.NewGuid(), AgentRunId = run.Id, NodeKey = "test", NodeType = "test", Status = "Running" });
        db.AddRange(user, run);
        await db.SaveChangesAsync();
        return run;
    }
}
