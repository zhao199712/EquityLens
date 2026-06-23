using Microsoft.EntityFrameworkCore;
using EquityLens.Api.Data;

namespace EquityLens.Api.Services.Chat;

public sealed class FinancialDataService : IFinancialDataService
{
    private readonly EquityLensDbContext _db;

    public FinancialDataService(EquityLensDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<FinancialDataResult>> QueryAsync(
        FinancialDataQuery query,
        CancellationToken cancellationToken = default)
    {
        var statements = await _db.FinancialStatements
            .Where(fs => fs.Security.Ticker == query.Ticker
                && fs.StatementType == query.StatementType
                && fs.FiscalYear == query.FiscalYear
                && (query.FiscalQuarter == null || fs.FiscalQuarter == query.FiscalQuarter))
            .Include(fs => fs.LineItems)
            .Include(fs => fs.Security)
            .OrderByDescending(fs => fs.PeriodEndDate)
            .ToListAsync(cancellationToken);

        return statements.Select(fs => new FinancialDataResult(
            fs.Security.Ticker,
            fs.StatementType,
            fs.PeriodType,
            fs.FiscalYear,
            fs.FiscalQuarter,
            fs.PeriodEndDate,
            fs.Currency,
            fs.LineItems.Select(li => new FinancialLineItemDto(
                li.Code, li.Name, li.Amount, li.Unit)).ToList())).ToList();
    }

    public async Task<IReadOnlyList<string>> SearchTickersAsync(
        string hint,
        CancellationToken cancellationToken = default)
    {
        return await _db.Securities
            .Where(s => s.Ticker.Contains(hint) || s.Name.Contains(hint))
            .OrderBy(s => s.Ticker)
            .Select(s => $"{s.Ticker} ({s.Name})")
            .Take(10)
            .ToListAsync(cancellationToken);
    }
}
