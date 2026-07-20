namespace EquityLens.Api.Services.FinancialData;

public sealed record FinMindDividendImportResult(int Events, int CashFlows);
public interface IFinMindDividendImportService
{
    Task<FinMindDividendImportResult> ImportAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
