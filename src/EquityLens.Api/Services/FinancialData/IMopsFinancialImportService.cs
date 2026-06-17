namespace EquityLens.Api.Services.FinancialData;

public sealed record MopsFinancialImportResult(int StatementsCreated, int LineItemsCreated, int Succeeded, int Failed);

public interface IMopsFinancialImportService
{
    Task<MopsFinancialImportResult> ImportAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
