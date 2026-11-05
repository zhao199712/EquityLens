using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EquityLens.Api.Bootstrap;
using EquityLens.Api.Contracts.Risk;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.BackgroundWorkers;
using EquityLens.Api.Services.Redis;
using EquityLens.Api.Services.RiskAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using StackExchange.Redis;

namespace EquityLens.Risk.IntegrationTests;

public sealed class RiskPipelineTests
{
    [Fact]
    public async Task Database_Redis_Python_ResultWorker_PersistsOwnedRiskWithoutFallback()
    {
        var postgres = Required("RISK_TEST_POSTGRES");
        var redisAddress = Required("RISK_TEST_REDIS");
        var redisUrl = Required("RISK_TEST_REDIS_URL");
        var databaseName = $"equitylens_risk_ci_{Guid.NewGuid():N}";
        var prefix = $"equitylens:risk-ci:{Guid.NewGuid():N}";
        var admin = new NpgsqlConnectionStringBuilder(postgres) { Database = "postgres", Pooling = false };
        await using var adminConnection = new NpgsqlConnection(admin.ConnectionString);
        using var redis = await ConnectionMultiplexer.ConnectAsync(redisAddress);
        await adminConnection.OpenAsync();
        await new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", adminConnection).ExecuteNonQueryAsync();
        try
        {
            var dbConnection = new NpgsqlConnectionStringBuilder(postgres) { Database = databaseName, Pooling = false };
            // Register real portfolio services/repositories. Never load application secrets or start unrelated workers.
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing", ContentRootPath = Path.GetTempPath() });
            builder.Configuration.Sources.Clear();
            builder.Logging.ClearProviders();
            builder.Services.AddDbContext<EquityLensDbContext>(o => o.UseNpgsql(dbConnection.ConnectionString, p => p.UseVector()));
            builder.AddEquityLensPortfolios();
            builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
            builder.Services.Configure<RedisOptions>(o => { o.ConnectionString = redisAddress; o.JobsStreamKey = prefix + ":background"; });
            builder.Services.Configure<RiskPythonOptions>(o =>
            {
                o.PrimaryEnabled = true; o.JobsStreamKey = prefix + ":jobs"; o.JobsConsumerGroup = "ci-python";
                o.ResultsStreamKey = prefix + ":results"; o.ResultsConsumerGroup = "ci-results";
                o.DeadLetterStreamKey = prefix + ":dead-letter"; o.PayloadKeyPrefix = prefix; o.ResultPollSeconds = 1;
            });
            builder.Services.AddScoped<IBackgroundJobQueue, RedisBackgroundJobQueue>();
            builder.Services.AddScoped<IRiskPythonShadowQueue, RedisRiskPythonShadowQueue>();
            builder.Services.AddScoped<IRiskShadowComparisonService, RiskShadowComparisonService>();
            await using var services = builder.Services.BuildServiceProvider();
            var options = services.GetRequiredService<IOptions<RiskPythonOptions>>();
            var userId = Guid.NewGuid();
            var portfolioId = Guid.NewGuid();
            var from = new DateOnly(2025, 1, 1);
            var to = from.AddDays(359);
            using (var scope = services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<EquityLensDbContext>();
                await db.Database.MigrateAsync();
                await SeedAsync(db, userId, portfolioId, from);
            }
            await using var python = new PythonProcess(["-m", "equitylens_mathematics.worker"], new Dictionary<string, string>
            {
                ["REDIS_URL"] = redisUrl, ["RISK_JOBS_STREAM"] = options.Value.JobsStreamKey,
                ["RISK_JOBS_GROUP"] = options.Value.JobsConsumerGroup, ["RISK_RESULTS_STREAM"] = options.Value.ResultsStreamKey,
                ["RISK_DEAD_LETTER_STREAM"] = options.Value.DeadLetterStreamKey, ["RISK_PAYLOAD_PREFIX"] = prefix,
                ["RISK_WORKER_CONSUMER"] = "ci-python-worker",
            });
            using var resultWorker = new RiskPythonShadowResultWorker(services.GetRequiredService<IServiceScopeFactory>(),
                services.GetRequiredService<ILogger<RiskPythonShadowResultWorker>>(), options);
            await resultWorker.StartAsync(CancellationToken.None);
            try
            {
                await UntilAsync(async () => await redis.GetDatabase().KeyExistsAsync(options.Value.JobsStreamKey)
                    && await redis.GetDatabase().KeyExistsAsync(options.Value.ResultsStreamKey), python);
                Guid runId;
                using (var scope = services.CreateScope())
                {
                    var service = scope.ServiceProvider.GetRequiredService<IRiskCalculationRunService>();
                    var created = await service.CreateAsync(portfolioId, new CreateRiskCalculationRequest("risk", from, to, 1000), userId);
                    Assert.True(created.IsSuccess, created.ErrorMessage);
                    Assert.Equal("Running", created.Value!.Status); // A synchronous C# fallback must not pass.
                    Assert.Equal(0, created.Value.FallbackDepth);
                    runId = created.Value.Id;
                }
                RiskCalculationRunResponse? completed = null;
                await UntilAsync(async () =>
                {
                    using var scope = services.CreateScope();
                    var found = await scope.ServiceProvider.GetRequiredService<IRiskCalculationRunService>().GetAsync(portfolioId, runId, userId);
                    completed = found.Value;
                    return completed?.Status is "Completed" or "Failed";
                }, python);
                Assert.Equal("Completed", completed!.Status);
                Assert.Equal(100, completed.ProgressPercent);
                Assert.Equal(0, completed.FallbackDepth);
                Assert.Null(completed.FallbackReason);
                Assert.Null(completed.ErrorCode);
                Assert.Null(completed.ErrorMessage);
                Assert.Equal("VT-GARCH-t + Joint-Vector FHS", completed.SelectedModel);
                Assert.NotNull(completed.CompletedAtUtc);
                RiskContractTests.AssertCurrentRisk(completed.Result!.Value, portfolioId);

                // Inspect the actual C# queue payload, not a separate serializer imitation.
                var queue = redis.GetDatabase();
                var job = Assert.Single(await queue.StreamRangeAsync(options.Value.JobsStreamKey));
                var fields = job.Values.ToDictionary(v => v.Name.ToString(), v => v.Value.ToString());
                Assert.Equal(runId.ToString(), fields["calculationRunId"]);
                Assert.Equal("risk", fields["operation"]);
                Assert.Equal("1", fields["schemaVersion"]);
                var raw = Decode((byte[])(await queue.StringGetAsync(fields["inputKey"]))!);
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
                Assert.Equal(hash, fields["inputHash"]);
                Assert.Equal(hash, completed.InputHash);
                var input = JsonSerializer.Deserialize<RiskBacktestEngineInput>(raw, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                // 2317 has no price on day 42: the session is kept and its price carried forward
                // (zero return), so 360 price dates yield 359 returns instead of merging two days.
                Assert.Equal(359, input.ReturnDates.Count);
                var suspended = input.ReturnDates.ToList().IndexOf(from.AddDays(42));
                Assert.Equal(41, suspended);
                Assert.All(input.AssetReturns, row => Assert.Equal(359, row.Count));
                Assert.Equal(0m, input.AssetReturns[1][suspended]);
                Assert.NotEqual(0m, input.AssetReturns[0][suspended]);
                for (var day = 0; day < input.PortfolioReturns.Count; day++)
                {
                    // Cross-language contract enforced by the Python input validator.
                    var expected = input.Weights
                        .Select((weight, asset) => (double)weight * (Math.Exp((double)input.AssetReturns[asset][day]) - 1d))
                        .Sum();
                    Assert.InRange((double)input.PortfolioReturns[day] - expected, -1e-12, 1e-12);
                }
                Assert.Equal(1m, input.Weights.Sum(), 10);
                Assert.Equal(new[] { "2330", "2317" }, input.Assets!.Select(a => a.Ticker));
                Assert.All(input.Assets!, asset => Assert.False(string.IsNullOrWhiteSpace(asset.SecurityName)));
                Assert.Equal(to.ToString("yyyy-MM-dd"), completed.Result.Value.GetProperty("dataAsOfDate").GetString());
                var conditional = completed.Result.Value.GetProperty("conditionalRisk");
                Assert.Equal(2, conditional.GetProperty("holdings").GetArrayLength());
                foreach (var item in conditional.GetProperty("holdings").EnumerateArray())
                    Assert.Equal(input.LookbackDays, item.GetProperty("dataPointCount").GetInt32()); // Conditional fit uses the last 252 returns.
                Assert.Equal(359, completed.Result.Value.GetProperty("dailyLogReturns").GetArrayLength());

                var result = Assert.Single(await queue.StreamRangeAsync(options.Value.ResultsStreamKey));
                var resultFields = result.Values.ToDictionary(v => v.Name.ToString(), v => v.Value.ToString());
                Assert.Equal(fields["jobId"], resultFields["jobId"]);
                Assert.Equal(runId.ToString(), resultFields["calculationRunId"]);
                Assert.Equal("Completed", resultFields["status"]);
                Assert.Equal(0, await queue.StreamLengthAsync(options.Value.DeadLetterStreamKey));
                await UntilAsync(async () => (await queue.StreamPendingAsync(options.Value.JobsStreamKey, options.Value.JobsConsumerGroup)).PendingMessageCount == 0
                    && (await queue.StreamPendingAsync(options.Value.ResultsStreamKey, options.Value.ResultsConsumerGroup)).PendingMessageCount == 0, python);
                var pythonResult = Decode((byte[])(await queue.StringGetAsync(resultFields["resultKey"]))!);
                using var pythonJson = JsonDocument.Parse(pythonResult);
                Assert.True(JsonElement.DeepEquals(pythonJson.RootElement.GetProperty("value"), completed.Result.Value));
                using (var scope = services.CreateScope())
                {
                    var service = scope.ServiceProvider.GetRequiredService<IRiskCalculationRunService>();
                    Assert.False((await service.GetAsync(portfolioId, runId, Guid.NewGuid())).IsSuccess);
                    var db = scope.ServiceProvider.GetRequiredService<EquityLensDbContext>();
                    var persisted = await db.RiskCalculationRuns.AsNoTracking().SingleAsync();
                    Assert.Equal(userId, persisted.RequestedByUserId);
                    Assert.Equal(portfolioId, persisted.PortfolioId);
                    Assert.Equal(runId, persisted.Id);
                    Assert.Equal(2, await db.PortfolioHoldings.CountAsync());
                    Assert.All(await db.PortfolioHoldings.ToListAsync(), h => Assert.Equal(10m, h.Quantity));
                }
                RiskContractTests.SaveArtifact("pipeline-input.json", raw);
                RiskContractTests.SaveArtifact("pipeline-python-result.json", pythonResult);
                RiskContractTests.SaveArtifact("pipeline-persisted-response.json", JsonSerializer.Serialize(completed, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            }
            finally { await resultWorker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15)); }
        }
        finally
        {
            // Only this test's UUID-prefixed keys/database; never flush Redis or drop a configured database.
            foreach (var endpoint in redis.GetEndPoints())
            {
                var keys = redis.GetServer(endpoint).Keys(pattern: prefix + ":*").ToArray();
                if (keys.Length > 0) await redis.GetDatabase().KeyDeleteAsync(keys);
            }
            await new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\"", adminConnection).ExecuteNonQueryAsync();
        }
    }

    private static async Task UntilAsync(Func<Task<bool>> condition, PythonProcess python)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        while (!await condition())
        {
            Assert.False(python.HasExited, "Python worker exited; see worker logs");
            await Task.Delay(200, timeout.Token);
        }
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value : throw new InvalidOperationException($"Set {name} to an isolated test service; this suite never silently skips.");

    private static string Decode(byte[] bytes)
    {
        using var stream = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static async Task SeedAsync(EquityLensDbContext db, Guid userId, Guid portfolioId, DateOnly from)
    {
        db.Users.Add(new AppUser { Id = userId, Email = "risk-ci@example.invalid", DisplayName = "Risk CI" });
        db.Portfolios.Add(new Portfolio { Id = portfolioId, OwnerUserId = userId, Name = "Synthetic CI portfolio", BaseCurrency = "TWD" });
        for (var asset = 0; asset < 2; asset++)
        {
            var id = Guid.Parse(asset == 0 ? "11111111-1111-4111-8111-111111111111" : "22222222-2222-4222-8222-222222222222");
            db.Securities.Add(new Security { Id = id, Ticker = asset == 0 ? "2330" : "2317", Name = asset == 0 ? "台積電" : "鴻海", Exchange = "TWSE", Currency = "TWD", Industry = asset == 0 ? "半導體" : "電子零組件" });
            db.PortfolioHoldings.Add(new PortfolioHolding { Id = Guid.NewGuid(), PortfolioId = portfolioId, SecurityId = id, Quantity = 10m, AverageCost = 100m, CostCurrency = "TWD" });
            var price = 100m;
            for (var day = 0; day < 360; day++)
            {
                var logReturn = asset == 0 ? .001 * (day % 7 - 3) : .0008 * ((day + 2) % 9 - 4);
                price *= (decimal)Math.Exp(logReturn);
                if (asset == 1 && day == 42) continue;
                db.MarketPrices.Add(new MarketPrice { Id = Guid.NewGuid(), SecurityId = id,
                    PriceTime = from.AddDays(day).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                    Open = price, High = price, Low = price, Close = price, AdjustedClose = price,
                    Interval = "1d", DataSource = "synthetic-ci" });
            }
        }
        await db.SaveChangesAsync();
    }
}
