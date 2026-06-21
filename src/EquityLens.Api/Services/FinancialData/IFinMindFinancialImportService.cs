namespace EquityLens.Api.Services.FinancialData;

public sealed record FinMindFinancialImportResult(int TotalStatements, int TotalLineItems, int Succeeded, int Failed);

public interface IFinMindFinancialImportService
{
    Task<FinMindFinancialImportResult> ImportAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
