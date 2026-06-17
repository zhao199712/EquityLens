using System.Globalization;
using System.Text.Json;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.MarketData;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Services.FinancialData;

public sealed class FinMindFinancialImportService : IFinMindFinancialImportService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly IReadOnlyDictionary<string, (string Dataset, string StatementType)> DatasetMap =
        new Dictionary<string, (string, string)>
        {
            ["TaiwanStockFinancialStatements"] = ("TaiwanStockFinancialStatements", "IncomeStatement"),
            ["TaiwanStockBalanceSheet"] = ("TaiwanStockBalanceSheet", "BalanceSheet"),
            ["TaiwanStockCashFlowsStatement"] = ("TaiwanStockCashFlowsStatement", "CashFlow"),
        };

    private readonly EquityLensDbContext _dbContext;
    private readonly HttpClient _httpClient;
    private readonly FinMindOptions _options;

    public FinMindFinancialImportService(
        EquityLensDbContext dbContext,
        HttpClient httpClient,
        IOptions<FinMindOptions> options)
    {
        _dbContext = dbContext;
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<FinMindFinancialImportResult> ImportAsync(
        DateOnly from, DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var securities = await _dbContext.Securities
            .Where(s => (s.Exchange == "TWSE" || s.Exchange == "TPEX") && s.IsActive)
            .OrderBy(s => s.Ticker)
            .ToListAsync(cancellationToken);

        Console.WriteLine($"找到 {securities.Count} 檔台股證券");

        var totalStatements = 0;
        var totalLineItems = 0;
        var succeeded = 0;
        var failed = 0;

        foreach (var security in securities)
        {
            foreach (var (dataset, (datasetName, statementType)) in DatasetMap)
            {
                try
                {
                    var result = await ImportDatasetAsync(
                        security, datasetName, statementType, from, to, cancellationToken);

                    totalStatements += result.Statements;
                    totalLineItems += result.LineItems;
                    succeeded++;
                    Console.WriteLine($"  ✓ {security.Ticker} / {statementType}: {result.Statements} statements, {result.LineItems} line items");
                }
                catch (Exception ex)
                {
                    failed++;
                    var inner = ex.InnerException?.Message ?? "";
                    Console.WriteLine($"  ✗ {security.Ticker} / {statementType}: {ex.Message} {inner}");
                }

                await Task.Delay(50, cancellationToken);
            }
        }

        return new FinMindFinancialImportResult(totalStatements, totalLineItems, succeeded, failed);
    }

    private async Task<(int Statements, int LineItems)> ImportDatasetAsync(
        Security security,
        string datasetName,
        string statementType,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();

        var queryParams = new Dictionary<string, string?>
        {
            ["dataset"] = datasetName,
            ["data_id"] = security.Ticker,
            ["start_date"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["end_date"] = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };

        if (!string.IsNullOrWhiteSpace(_options.Token))
            queryParams["token"] = _options.Token;

        var url = QueryHelpers.AddQueryString("/api/v4/data", queryParams);
        var fullUrl = $"{_options.BaseUrl.TrimEnd('/')}{url}";

        using var response = await _httpClient.GetAsync(fullUrl, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"FinMind returned {(int)response.StatusCode}: {body}");

        var payload = JsonSerializer.Deserialize<FinMindApiResponse>(body, JsonOptions);

        if (payload?.Data is null || payload.Data.Count == 0)
            return (0, 0);

        var grouped = payload.Data
            .GroupBy(d => d.Date)
            .OrderBy(g => g.Key)
            .ToList();

        // Find which quarters already exist in DB
        var existingKeys = await _dbContext.FinancialStatements
            .Where(s => s.SecurityId == security.Id
                && s.StatementType == statementType
                && s.PeriodType == "Quarterly")
            .Select(s => new { s.FiscalYear, s.FiscalQuarter })
            .ToListAsync(cancellationToken);

        var existingSet = existingKeys
            .Select(k => (k.FiscalYear, k.FiscalQuarter!.Value))
            .ToHashSet();

        var statementsCreated = 0;
        var lineItemsCreated = 0;

        foreach (var group in grouped)
        {
            var date = group.Key;
            var (fiscalYear, fiscalQuarter) = GetFiscalPeriod(date);

            if (existingSet.Contains((fiscalYear, fiscalQuarter)))
                continue;

            var newStatement = new FinancialStatement
            {
                Id = Guid.NewGuid(),
                SecurityId = security.Id,
                StatementType = statementType,
                PeriodType = "Quarterly",
                FiscalYear = fiscalYear,
                FiscalQuarter = fiscalQuarter,
                PeriodEndDate = date,
                Currency = "TWD",
                DataSource = "FinMind",
            };

            foreach (var entry in group)
            {
                var code = TruncateCode(entry.Type);
                var name = entry.OriginName ?? entry.Type;

                newStatement.LineItems.Add(new FinancialLineItem
                {
                    Id = Guid.NewGuid(),
                    FinancialStatementId = newStatement.Id,
                    Code = code,
                    Name = name,
                    Amount = Math.Abs(entry.Value),
                    Unit = entry.Type.EndsWith("_per", StringComparison.Ordinal) ? "Percent" : "TWD",
                });
                lineItemsCreated++;
            }

            _dbContext.FinancialStatements.Add(newStatement);
            statementsCreated++;
        }

        if (statementsCreated > 0)
            await _dbContext.SaveChangesAsync(cancellationToken);

        return (statementsCreated, lineItemsCreated);
    }

    private static string TruncateCode(string type)
    {
        return type.Length <= 32 ? type : type[..24] + type[^8..];
    }

    private static (int Year, int Quarter) GetFiscalPeriod(DateOnly date)
    {
        return (date.Year, (date.Month - 1) / 3 + 1);
    }

    private sealed record FinMindApiResponse(int Status, string? Msg, List<FinMindRow>? Data);

    private sealed record FinMindRow(DateOnly Date, string Type, decimal Value, string OriginName);
}
