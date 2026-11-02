using EquityLens.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.Agents;

/// <summary>使用獨立 scope 續約，不與節點共用 DbContext。</summary>
public sealed class AgentRunLeaseHeartbeat(IServiceScopeFactory scopes, TimeProvider clock)
{
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan RenewalInterval = TimeSpan.FromMinutes(1);

    public async Task<bool> RenewAsync(Guid runId, string owner, long version, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EquityLensDbContext>();
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now + LeaseDuration;
        if (db.Database.IsRelational())
            return await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE agent_run SET lease_expires_at_utc = {expires}
                WHERE id = {runId} AND lease_owner = {owner} AND lease_version = {version}
                  AND lease_expires_at_utc > {now} AND status = 'Running'
                """, ct) == 1;
        var run = await db.AgentRuns.SingleOrDefaultAsync(x => x.Id == runId, ct);
        if (run is null || run.LeaseOwner != owner || run.LeaseVersion != version
            || run.LeaseExpiresAtUtc <= now || run.Status != AgentRunStatuses.Running) return false;
        run.LeaseExpiresAtUtc = expires;
        try { await db.SaveChangesAsync(ct); return true; }
        catch (DbUpdateConcurrencyException) { return false; }
    }

    public async Task RunAsync(Guid runId, string owner, long version, CancellationTokenSource leaseLost, CancellationToken stop)
    {
        try
        {
            using var timer = new PeriodicTimer(RenewalInterval, clock);
            while (await timer.WaitForNextTickAsync(stop))
            {
                if (await RenewAsync(runId, owner, version, stop)) continue;
                leaseLost.Cancel();
                return;
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception) { leaseLost.Cancel(); } // 無法證明持有租約時停止；寫入端仍有交易檢查。
    }
}
