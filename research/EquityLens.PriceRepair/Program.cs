using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.MarketData;
using EquityLens.Api.Services.MarketPrices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector.EntityFrameworkCore;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: price-repair <backup.json> <new-output-directory>. Requires EQUITYLENS_PRICE_REPAIR_POSTGRES (isolated PostgreSQL admin connection).");
    return 2;
}
var output = Path.GetFullPath(args[1]);
if (Directory.Exists(output)) throw new InvalidOperationException("Use a new output directory to preserve prior evidence.");
Directory.CreateDirectory(output);
var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
options.Converters.Add(new UtcDateTimeConverter());
var input = await File.ReadAllTextAsync(args[0]);
var backup = JsonSerializer.Deserialize<Backup>(input, options)!;
if (backup.Securities.Count != 57 || backup.Securities.Any(x => !x.IsActive || x.Exchange is not ("TWSE" or "TPEX")))
    throw new InvalidOperationException("Expected the backed-up 57 active Taiwan securities.");
var baseConnection = Environment.GetEnvironmentVariable("EQUITYLENS_PRICE_REPAIR_POSTGRES")
    ?? throw new InvalidOperationException("EQUITYLENS_PRICE_REPAIR_POSTGRES must point to the isolated PostgreSQL test instance.");
var connection = new NpgsqlConnectionStringBuilder(baseConnection) { Database = "postgres", Pooling = false };
var dbName = "equitylens_price_repair_" + Guid.NewGuid().ToString("N");
await using (var admin = new NpgsqlConnection(connection.ConnectionString))
{
    await admin.OpenAsync();
    await using var create = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", admin);
    await create.ExecuteNonQueryAsync();
}
connection.Database = dbName;
await File.WriteAllTextAsync(Path.Combine(output, "database.json"), JsonSerializer.Serialize(new
    { database = dbName, backupSha256 = TaiwanPriceImportCoordinator.Hash(input), from = "2019-11-01", to = "2026-09-30" }));
var dbOptions = new DbContextOptionsBuilder<EquityLensDbContext>().UseNpgsql(connection.ConnectionString, x => x.UseVector()).Options;
var contexts = new RepairContexts(dbOptions);
await using (var seed = contexts.CreateDbContext())
{
    await seed.Database.MigrateAsync();
    seed.Securities.AddRange(backup.Securities);
    seed.MarketPrices.AddRange(backup.Prices);
    await seed.SaveChangesAsync();
}
await File.WriteAllTextAsync(Path.Combine(output, "before.json"), input);
var finOptions = Options.Create(new FinMindOptions { BaseUrl = "https://api.finmindtrade.com",
    Token = Environment.GetEnvironmentVariable("FINMIND_TOKEN") ?? "" });
var replayPath = Environment.GetEnvironmentVariable("EQUITYLENS_PRICE_REPAIR_SOURCE_REPLAY");
var replay = replayPath is null ? null : new FrozenPriceSourceHandler(replayPath);
using var finHttp = (replay is null ? new HttpClient() : new HttpClient(replay, disposeHandler: false));
finHttp.BaseAddress = new Uri(finOptions.Value.BaseUrl); finHttp.Timeout = TimeSpan.FromSeconds(90);
using var yahooHttp = replay is null ? new HttpClient() : new HttpClient(replay, disposeHandler: false);
yahooHttp.BaseAddress = new Uri("https://query1.finance.yahoo.com"); yahooHttp.Timeout = TimeSpan.FromSeconds(90);
yahooHttp.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 EquityLens/1.0");
using var officialHttp = replay is null ? new HttpClient() : new HttpClient(replay, disposeHandler: false);
officialHttp.Timeout = TimeSpan.FromSeconds(90);
officialHttp.DefaultRequestHeaders.UserAgent.ParseAdd("EquityLens/1.0");
Directory.CreateDirectory(Path.Combine(output, "sources"));
var succeeded = 0;
var failed = 0;
var stopped = false;
var outcomes = new List<object>();
foreach (var security in backup.Securities.OrderBy(x => x.Ticker != "2330").ThenBy(x => x.Ticker))
{
    replay?.Select(security.Ticker);
    var finProvider = new FinMindMarketDataProvider(finHttp, finOptions);
    var yahooProvider = new YahooFinanceMarketDataProvider(yahooHttp);
    var officialProvider = new TaiwanPriceEvidenceProvider(officialHttp, finOptions);
    var importer = new TaiwanPriceImportCoordinator(contexts, [finProvider, yahooProvider], officialProvider);

    var before = await StateAsync(security.Id);
    try
    {
        var result = await importer.ImportAsync(security.Id, new(2019, 11, 1), new(2026, 9, 30), default);
        var after = await StateAsync(security.Id);
        if (!result.IsSuccess)
        {
            if (before.Fingerprint != after.Fingerprint)
                throw new InvalidOperationException("Failed import changed committed state.");
            failed++;
            outcomes.Add(new { security.Ticker, success = false, result.ErrorCode, result.ErrorMessage });
        }
        else
        {
            var simulatedFailure = Environment.GetEnvironmentVariable("EQUITYLENS_PRICE_REPAIR_SIMULATE_VERIFICATION_FAILURE") == security.Ticker;
            var invalid = simulatedFailure || after.Rows.Count == 0 || after.Rows.Any(x => x.Close <= 0 || x.AdjustedClose is null or <= 0 ||
                x.PriceAdjustmentBatchId != after.Security.PriceAdjustmentBatchId) ||
                after.Security.PricesVerifiedThrough != new DateOnly(2026, 9, 30);
            if (invalid)
            {
                await RestoreAsync(before, after);
                throw new InvalidOperationException("Post-commit quality degraded; restored this security and stopped the batch.");
            }
            succeeded++;
            outcomes.Add(new { security.Ticker, success = true, response = result.Value });
        }
    }
    catch (Exception ex)
    {
        var after = await StateAsync(security.Id);
        if (before.Fingerprint != after.Fingerprint)
        {
            await RestoreAsync(before, after);
            stopped = true;
        }
        // 交易或復原失敗屬批次停止條件；单股正常品質/來源不足由 Result 處理並繼續。
        outcomes.Add(new { security.Ticker, success = false, fatal = ex.GetType().Name });
        failed++; stopped = true;
    }
    await File.WriteAllTextAsync(Path.Combine(output, "sources", security.Ticker + ".json"), JsonSerializer.Serialize(new
    { security.Ticker, raw = finProvider.LastPriceEvidenceJson, yahoo = yahooProvider.LastAdjustmentEvidenceJson,
        official = officialProvider.LastEvidenceJson }, options));
    await File.WriteAllTextAsync(Path.Combine(output, "outcomes.json"), JsonSerializer.Serialize(outcomes, options));
    Console.WriteLine($"{security.Ticker}: {succeeded} succeeded, {failed} failed");
    if (stopped) break;
    await Task.Delay(1000);
}
await FreezeAsync();
await File.WriteAllTextAsync(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new
    { succeeded, failed, stopped, replay = replay is not null, sourceDirectory = replayPath, database = dbName }, options));
Console.WriteLine($"Frozen backup rehearsal: {succeeded} succeeded, {failed} failed. Database {dbName} retained for inspection.");
return stopped ? 1 : 0;

async Task<PriceState> StateAsync(Guid id)
{
    await using var db = contexts.CreateDbContext();
    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
    var s = await db.Securities.AsNoTracking().SingleAsync(x => x.Id == id);
    var p = await db.MarketPrices.AsNoTracking().Where(x => x.SecurityId == id && x.Interval == "1d").ToListAsync();
    return new(s, p, TaiwanPriceImportCoordinator.Fingerprint(s, p));
}

async Task RestoreAsync(PriceState before, PriceState expectedAfter)
{
    await using var db = contexts.CreateDbContext();
    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
    var s = (await db.Securities.FromSqlInterpolated($"SELECT * FROM security WHERE id = {before.Security.Id} FOR UPDATE").ToListAsync()).Single();
    var rows = await db.MarketPrices.Where(x => x.SecurityId == s.Id && x.Interval == "1d").ToListAsync();
    if (TaiwanPriceImportCoordinator.Fingerprint(s, rows) != expectedAfter.Fingerprint)
        throw new InvalidOperationException("Concurrent change detected; refusing restoration.");
    db.MarketPrices.RemoveRange(rows);
    await db.SaveChangesAsync();
    db.MarketPrices.AddRange(before.Rows);
    s.PricesSource = before.Security.PricesSource; s.PricesSyncedAtUtc = before.Security.PricesSyncedAtUtc;
    s.PriceAdjustmentBatchId = before.Security.PriceAdjustmentBatchId; s.PricesVerifiedThrough = before.Security.PricesVerifiedThrough;
    await db.SaveChangesAsync();
    await tx.CommitAsync();
    // 調整批次審計保留，以供追查這次被撤回的提交。
    var restored = await StateAsync(s.Id);
    if (restored.Fingerprint != before.Fingerprint) throw new InvalidOperationException("Restoration verification failed.");
}

async Task FreezeAsync()
{
    await using var db = contexts.CreateDbContext();
    await db.Database.OpenConnectionAsync();
    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
    await using var command = db.Database.GetDbConnection().CreateCommand();
    command.Transaction = tx.GetDbTransaction();
    command.CommandText = """
        SELECT json_build_object('capturedAt',now(),
          'securities',(SELECT json_agg(s ORDER BY ticker) FROM security s),
          'prices',(SELECT json_agg(p ORDER BY p.security_id,p.interval,p.price_time) FROM market_price p));
        """;
    var snapshot = (string)(await command.ExecuteScalarAsync())!;
    await File.WriteAllTextAsync(Path.Combine(output, "snapshot.json"), snapshot);
    await File.WriteAllTextAsync(Path.Combine(output, "snapshot.sha256"), TaiwanPriceImportCoordinator.Hash(snapshot));
    var batches = await db.PriceAdjustmentBatches.AsNoTracking().ToListAsync();
    await File.WriteAllTextAsync(Path.Combine(output, "adjustment-batches.json"), JsonSerializer.Serialize(batches, options));
}

internal sealed record Backup(List<Security> Securities, List<MarketPrice> Prices);
internal sealed record PriceState(Security Security, List<MarketPrice> Rows, string Fingerprint);
internal sealed class RepairContexts(DbContextOptions<EquityLensDbContext> options) : IDbContextFactory<EquityLensDbContext>
{
    public EquityLensDbContext CreateDbContext() => new(options);
    public Task<EquityLensDbContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(CreateDbContext());
}

internal sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => reader.GetDateTimeOffset().UtcDateTime;
    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToUniversalTime());
}
