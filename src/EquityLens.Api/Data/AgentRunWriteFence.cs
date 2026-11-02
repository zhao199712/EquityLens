using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Data;

/// <summary>以同一交易中的租約列鎖保護執行結果，避免過期 worker 寫入。</summary>
public sealed record AgentRunWriteFence(Guid RunId, string Owner, long Version, TimeProvider Clock)
{
    public async Task<int> SaveAsync(EquityLensDbContext db, Func<Task<int>> save, CancellationToken ct)
    {
        var now = Clock.GetUtcNow().UtcDateTime;
        if (!db.Database.IsRelational())
        {
            // InMemory 僅供邏輯測試；交易與競態保證由 PostgreSQL 整合測試驗證。
            if (!await db.AgentRuns.AsNoTracking().AnyAsync(x => x.Id == RunId && x.LeaseOwner == Owner
                    && x.LeaseVersion == Version && x.LeaseExpiresAtUtc > now && x.Status == "Running", ct))
                throw new AgentRunLeaseLostException();
            return await save();
        }

        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct) : null;
        // UPDATE 取得列鎖並重新評估 predicate；鎖持有至所有結果一併提交。
        var matched = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE agent_run SET lease_version = lease_version
            WHERE id = {RunId} AND lease_owner = {Owner} AND lease_version = {Version}
              AND lease_expires_at_utc > {now} AND status = 'Running'
            """, ct);
        if (matched != 1) throw new AgentRunLeaseLostException();
        var count = await save();
        if (transaction is not null) await transaction.CommitAsync(ct);
        return count;
    }
}

public sealed class AgentRunLeaseLostException : Exception
{
    public AgentRunLeaseLostException() : base("Agent run lease was lost; stale execution cannot commit.") { }
}
