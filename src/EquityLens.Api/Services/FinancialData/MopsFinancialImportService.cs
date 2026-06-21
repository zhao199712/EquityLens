using System.Globalization;
using System.Text.RegularExpressions;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.FinancialData;

public sealed partial class MopsFinancialImportService : IMopsFinancialImportService
{
    private const string BaseUrl = "https://mopsov.twse.com.tw/mops/web/";

    private static readonly (string Endpoint, string StatementType, string Label)[] Endpoints =
    [
        ("ajax_t164sb03", "BalanceSheet", "資產負債表"),
        ("ajax_t164sb04", "IncomeStatement", "綜合損益表"),
        ("ajax_t164sb05", "CashFlow", "現金流量表"),
    ];

    private readonly EquityLensDbContext _dbContext;
    private readonly HttpClient _httpClient;

    public MopsFinancialImportService(EquityLensDbContext dbContext, HttpClient httpClient)
    {
        _dbContext = dbContext;
        _httpClient = httpClient;
    }

    public async Task<MopsFinancialImportResult> ImportAsync(
        DateOnly from, DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var securities = await _dbContext.Securities
            .Where(s => s.Exchange == "TWSE" && s.IsActive)
            .OrderBy(s => s.Ticker)
            .ToListAsync(cancellationToken);

        Console.WriteLine($"找到 {securities.Count} 檔上市證券");

        var totalStatements = 0;
        var totalLineItems = 0;
        var succeeded = 0;
        var failed = 0;

        foreach (var security in securities)
        {
            foreach (var (endpoint, statementType, label) in Endpoints)
            {
                try
                {
                    var result = await ImportStatementAsync(
                        security, endpoint, statementType, from, to, cancellationToken);

                    totalStatements += result.Statements;
                    totalLineItems += result.LineItems;
                    succeeded++;
                    Console.WriteLine($"  ✓ {security.Ticker} / {label}: {result.Statements} statements, {result.LineItems} items");
                }
                catch (DbUpdateException ex)
                {
                    failed++;
                    var inner = ex.InnerException?.Message ?? "";
                    // Show the violating key values
                    var entries = ex.Entries?.Select(e => e.Entity.GetType().Name + ":" + (e.Entity is FinancialLineItem li ? li.Code + "@" + li.FinancialStatementId.ToString("N")[..8] : "")).ToList();
                    var entryInfo = entries != null && entries.Count > 0 ? string.Join(", ", entries) : "";
                    Console.WriteLine($"  ✗ {security.Ticker} / {label}: {inner} [{entryInfo}]");
                }
                catch (Exception ex)
                {
                    failed++;
                    var inner = ex.InnerException?.Message ?? "";
                    Console.WriteLine($"  ✗ {security.Ticker} / {label}: {ex.Message} {inner}");
                }

                await Task.Delay(200, cancellationToken);
            }
        }

        return new MopsFinancialImportResult(totalStatements, totalLineItems, succeeded, failed);
    }

    private async Task<(int Statements, int LineItems)> ImportStatementAsync(
        Security security,
        string endpoint,
        string statementType,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var statementsCreated = 0;
        var lineItemsCreated = 0;

        for (var year = from.Year; year <= to.Year; year++)
        {
            for (var season = 1; season <= 4; season++)
            {
                var periodEndDate = GetPeriodEndDate(year, season);
                if (periodEndDate < from || periodEndDate > to || periodEndDate > DateOnly.FromDateTime(DateTime.Today))
                    continue;

                var fiscalQuarter = season;
                var rocYear = year - 1911;

                // Check if already imported
                var exists = await _dbContext.FinancialStatements.AnyAsync(
                    s => s.SecurityId == security.Id
                        && s.StatementType == statementType
                        && s.PeriodType == "Quarterly"
                        && s.FiscalYear == year
                        && s.FiscalQuarter == fiscalQuarter,
                    cancellationToken);

                if (exists)
                    continue;

                // Fetch HTML from MOPS
                var formData = new Dictionary<string, string>
                {
                    ["encodeURIComponent"] = "1",
                    ["step"] = "1",
                    ["firstin"] = "1",
                    ["off"] = "1",
                    ["queryName"] = "co_id",
                    ["inpuType"] = "co_id",
                    ["TYPEK"] = "all",
                    ["isnew"] = "false",
                    ["co_id"] = security.Ticker,
                    ["year"] = rocYear.ToString(),
                    ["season"] = season.ToString(),
                };

                using var content = new FormUrlEncodedContent(formData);
                using var response = await _httpClient.PostAsync(
                    $"{BaseUrl}{endpoint}", content, cancellationToken);
                var html = await response.Content.ReadAsStringAsync(cancellationToken);

                if (html.Contains("查無資料", StringComparison.Ordinal))
                {
                    Console.WriteLine($"    ~ {security.Ticker} {year}Q{season}: 查無資料");
                    continue;
                }

                // Parse HTML table
                var items = ParseTableRows(html);
                if (items.Count == 0)
                {
                    Console.WriteLine($"    ~ {security.Ticker} {year}Q{season}: 無科目資料");
                    continue;
                }

                // Create financial statement
                var statement = new FinancialStatement
                {
                    Id = Guid.NewGuid(),
                    SecurityId = security.Id,
                    StatementType = statementType,
                    PeriodType = "Quarterly",
                    FiscalYear = year,
                    FiscalQuarter = fiscalQuarter,
                    PeriodEndDate = periodEndDate,
                    Currency = "TWD",
                    DataSource = "MOPS",
                };

                foreach (var (name, value) in items)
                {
                    if (!value.HasValue)
                        continue;

                    var code = TruncateCode(name);
                    // Handle duplicate codes within the same statement
                    var suffix = 0;
                    var finalCode = code;
                    while (statement.LineItems.Any(li => li.Code == finalCode))
                    {
                        suffix++;
                        finalCode = code.Length <= 28 ? $"{code}_{suffix}" : code[..28] + $"_{suffix}";
                    }

                    statement.LineItems.Add(new FinancialLineItem
                    {
                        Id = Guid.NewGuid(),
                        FinancialStatementId = statement.Id,
                        Code = finalCode,
                        Name = name,
                        Amount = Math.Abs(value.Value),
                        Unit = "TWD",
                    });
                    lineItemsCreated++;
                }

                if (statement.LineItems.Count > 0)
                {
                    _dbContext.FinancialStatements.Add(statement);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    statementsCreated++;
                    Console.WriteLine($"    ✓ {security.Ticker} {year}Q{season} {endpoint}: {statement.LineItems.Count} lines");
                }
            }
        }

        return (statementsCreated, lineItemsCreated);
    }

    [GeneratedRegex(@"<table[^>]*hasBorder[^>]*>(.*?)</table>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex TableRegex();

    [GeneratedRegex(@"<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex RowRegex();

    [GeneratedRegex(@"<t[dh][^>]*>(.*?)</t[dh]>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex CellRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.IgnoreCase)]
    private static partial Regex StripTagsRegex();

    private static List<(string Name, decimal? Value)> ParseTableRows(string html)
    {
        var result = new List<(string Name, decimal? Value)>();

        var tableMatch = TableRegex().Match(html);
        if (!tableMatch.Success)
            return result;

        var tableContent = tableMatch.Groups[1].Value;
        var rowMatches = RowRegex().Matches(tableContent);

        foreach (Match rowMatch in rowMatches)
        {
            var rowContent = rowMatch.Groups[1].Value;
            var cellMatches = CellRegex().Matches(rowContent);
            if (cellMatches.Count < 2)
                continue;

            var name = StripTags(cellMatches[0].Groups[1].Value).Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var valueStr = StripTags(cellMatches[1].Groups[1].Value).Trim();
            if (string.IsNullOrWhiteSpace(valueStr))
                continue;

            // Skip the first 4 header rows
            if (name.Contains("資產負債表", StringComparison.Ordinal) ||
                name.Contains("綜合損益表", StringComparison.Ordinal) ||
                name.Contains("現金流量表", StringComparison.Ordinal))
                continue;

            var value = ParseValue(valueStr);
            result.Add((name, value));
        }

        return result;
    }

    private static string StripTags(string raw)
    {
        return StripTagsRegex().Replace(raw, " ").Trim();
    }

    private static decimal? ParseValue(string raw)
    {
        if (raw.Contains("--", StringComparison.Ordinal) || raw.Contains("—", StringComparison.Ordinal))
            return null;

        var clean = raw
            .Replace(",", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal)
            .Trim();

        if (string.IsNullOrEmpty(clean))
            return null;

        // Handle parentheses for negative: (1,234) → -1234
        if (clean.StartsWith('(') && clean.EndsWith(')'))
        {
            clean = "-" + clean[1..^1];
        }

        if (decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
            return result;

        return null;
    }

    private static string TruncateCode(string name)
    {
        return name.Length <= 32 ? name : name[..24] + name[^8..];
    }

    private static DateOnly GetPeriodEndDate(int year, int season)
    {
        return season switch
        {
            1 => new DateOnly(year, 3, 31),
            2 => new DateOnly(year, 6, 30),
            3 => new DateOnly(year, 9, 30),
            _ => new DateOnly(year, 12, 31),
        };
    }
}
