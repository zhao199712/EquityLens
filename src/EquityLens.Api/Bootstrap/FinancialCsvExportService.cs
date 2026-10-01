using EquityLens.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace EquityLens.Api.Bootstrap;

public sealed class FinancialCsvExportService(EquityLensDbContext db)
{
    public async Task ExportAsync(string csvPath)
    {
        var fullPath = Path.GetFullPath(csvPath);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        await using var writer = new StreamWriter(fullPath, false, Encoding.UTF8);
        await writer.WriteLineAsync("ticker,company_name,exchange,statement_type,period_type,fiscal_year,fiscal_quarter,period_end_date,line_code,line_name,amount,currency,data_source");

        var query = from fli in db.FinancialLineItems
                    join fs in db.FinancialStatements on fli.FinancialStatementId equals fs.Id
                    join s in db.Securities on fs.SecurityId equals s.Id
                    orderby s.Ticker, fs.StatementType, fs.FiscalYear, fs.FiscalQuarter, fli.Code
                    select new
                    {
                        Ticker = s.Ticker,
                        CompanyName = s.Name,
                        Exchange = s.Exchange,
                        StatementType = fs.StatementType,
                        PeriodType = fs.PeriodType,
                        FiscalYear = fs.FiscalYear,
                        FiscalQuarter = fs.FiscalQuarter,
                        PeriodEndDate = fs.PeriodEndDate,
                        LineCode = fli.Code,
                        LineName = fli.Name,
                        Amount = fli.Amount,
                        Currency = fs.Currency,
                        DataSource = fs.DataSource
                    };

        var count = 0;
        await foreach (var row in query.AsAsyncEnumerable())
        {
            await writer.WriteAsync($"{EscapeCsv(row.Ticker)},{EscapeCsv(row.CompanyName)},{EscapeCsv(row.Exchange)},");
            await writer.WriteAsync($"{EscapeCsv(row.StatementType)},{EscapeCsv(row.PeriodType)},{row.FiscalYear},");
            await writer.WriteAsync($"{row.FiscalQuarter},{row.PeriodEndDate:yyyy-MM-dd},");
            await writer.WriteAsync($"{EscapeCsv(row.LineCode)},{EscapeCsv(row.LineName)},{row.Amount:F2},");
            await writer.WriteLineAsync($"{EscapeCsv(row.Currency)},{EscapeCsv(row.DataSource)}");
            count++;
        }

        Console.WriteLine($"===== 匯出完成 =====");
        Console.WriteLine($"筆數: {count} | 路徑: {fullPath}");
    }

    private static string EscapeCsv(string? value) =>
        value is null ? "" :
        value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
