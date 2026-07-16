using System.Text.Json;
using EquityLens.Api.Contracts.MarketPrices;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Documents;
using EquityLens.Api.Services.FinancialData;
using EquityLens.Api.Services.InvestorConferences;
using EquityLens.Api.Services.MarketPrices;
using EquityLens.Api.Services.Redis;

namespace EquityLens.Api.Services.AdminJobs;

public interface IBackgroundJobExecutor
{
    Task<JobResult> ExecuteAsync(JobRun jobRun, CancellationToken cancellationToken = default);
}

public sealed record JobResult(bool Success, string? ErrorMessage, Dictionary<string, object?> Summary);

public sealed class BackgroundJobExecutor : IBackgroundJobExecutor
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BackgroundJobExecutor> _logger;

    public BackgroundJobExecutor(IServiceProvider serviceProvider, ILogger<BackgroundJobExecutor> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<JobResult> ExecuteAsync(JobRun jobRun, CancellationToken cancellationToken = default)
    {
        try
        {
            return await ExecuteCoreAsync(jobRun, cancellationToken);
        }
        catch (JobCancelledException ex)
        {
            return new JobResult(false, ex.Message, new Dictionary<string, object?> { ["cancelled"] = true });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background job {JobId} failed.", jobRun.Id);
            return new JobResult(false, ex.Message, new Dictionary<string, object?>());
        }
    }

    private async Task<JobResult> ExecuteCoreAsync(JobRun jobRun, CancellationToken cancellationToken)
    {
        var payload = ParsePayload(jobRun.PayloadJson);

        switch (jobRun.JobType)
        {
            case "ImportConferences":
                {
                    var service = _serviceProvider.GetRequiredService<IConferenceImportService>();
                    var result = await service.ImportAllAsync(cancellationToken);
                    return new JobResult(true, null, ToSummary(result));
                }
            case "ChunkConferences":
                {
                    var service = _serviceProvider.GetRequiredService<IConferenceChunkingService>();
                    var result = await service.ChunkConferencesAsync(cancellationToken);
                    return new JobResult(true, null, ToSummary(result));
                }
            case "EmbedChunks":
                {
                    var service = _serviceProvider.GetRequiredService<IChunkEmbeddingService>();
                    var result = await service.EmbedMissingChunksAsync(cancellationToken);
                    return new JobResult(true, null, ToSummary(result));
                }
            case "ImportFinMindFinancials":
                {
                    var (from, to) = ParseDateRange(payload);
                    var service = _serviceProvider.GetRequiredService<IFinMindFinancialImportService>();
                    var result = await service.ImportAsync(from, to, cancellationToken);
                    return new JobResult(true, null, ToSummary(result));
                }
            case "ImportFinMindDividends":
                {
                    var (from, to) = ParseDateRange(payload);
                    var service = _serviceProvider.GetRequiredService<IFinMindDividendImportService>();
                    var result = await service.ImportAsync(from, to, cancellationToken);
                    return new JobResult(true, null, ToSummary(result));
                }
            case "ImportMopsFinancials":
                {
                    var (from, to) = ParseDateRange(payload);
                    var service = _serviceProvider.GetRequiredService<IMopsFinancialImportService>();
                    var result = await service.ImportAsync(from, to, cancellationToken);
                    return new JobResult(true, null, ToSummary(result));
                }
            case "ImportTwseReportFiles":
                {
                    var service = _serviceProvider.GetRequiredService<ITwseReportFileImportService>();
                    var result = await service.ImportAllAsync(cancellationToken);
                    return new JobResult(true, null, ToSummary(result));
                }
            case "ImportMarketPrices":
                {
                    var service = _serviceProvider.GetRequiredService<IMarketPriceService>();
                    var ticker = payload.GetValueOrDefault("ticker")?.ToString() ?? throw new InvalidOperationException("ticker is required");
                    var exchange = payload.GetValueOrDefault("exchange")?.ToString() ?? "TWSE";
                    var (from, to) = ParseDateRange(payload, DateOnly.FromDateTime(DateTime.Today.AddYears(-1)), DateOnly.FromDateTime(DateTime.Today));
                    var response = await service.ImportDailyPricesByTickerAsync(
                        new ImportMarketPricesByTickerRequest(
                            ticker, exchange, from, to,
                            Name: null, AssetType: null, Currency: null, Isin: null, Sector: null, Industry: null),
                        cancellationToken);
                    return new JobResult(
                        response.IsSuccess,
                        response.IsSuccess ? null : $"{response.ErrorCode}: {response.ErrorMessage}",
                        response.IsSuccess
                            ? new Dictionary<string, object?>
                            {
                                ["importedCount"] = response.Value?.ImportedCount ?? 0,
                                ["insertedCount"] = response.Value?.InsertedCount ?? 0,
                                ["updatedCount"] = response.Value?.UpdatedCount ?? 0,
                            }
                            : new Dictionary<string, object?>());
                }
            case "SyncTw0050Prices":
                {
                    var service = _serviceProvider.GetRequiredService<ITw0050PriceSyncService>();
                    var result = await service.SyncAsync(jobRun.Id, cancellationToken);
                    return new JobResult(
                        result.FailedCount == 0,
                        result.FailedCount == 0 ? null : $"0050 price sync completed with {result.FailedCount} failed constituent(s).",
                        ToSummary(result));
                }
            default:
                return new JobResult(false, $"Unknown job type: {jobRun.JobType}", new Dictionary<string, object?>());
        }
    }

    private static Dictionary<string, object?> ParsePayload(string? payloadJson) =>
        string.IsNullOrWhiteSpace(payloadJson)
            ? new Dictionary<string, object?>()
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(payloadJson, SerializerOptions)
              ?? new Dictionary<string, object?>();

    private static (DateOnly From, DateOnly To) ParseDateRange(
        Dictionary<string, object?> payload,
        DateOnly? defaultFrom = null,
        DateOnly? defaultTo = null)
    {
        var from = defaultFrom ?? new DateOnly(2023, 1, 1);
        var to = defaultTo ?? DateOnly.FromDateTime(DateTime.Today);

        if (payload.TryGetValue("from", out var fromValue) && fromValue is not null)
        {
            if (DateOnly.TryParse(fromValue.ToString(), out var fromParsed))
                from = fromParsed;
        }

        if (payload.TryGetValue("to", out var toValue) && toValue is not null)
        {
            if (DateOnly.TryParse(toValue.ToString(), out var toParsed))
                to = toParsed;
        }

        return (from, to);
    }

    private static Dictionary<string, object?> ToSummary(object result) =>
        JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(result, SerializerOptions), SerializerOptions)
        ?? new Dictionary<string, object?>();
}
