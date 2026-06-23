namespace EquityLens.Api.Services.Chat;

public sealed record FinancialDataQuery(
    string Ticker,
    string StatementType,
    int FiscalYear,
    int? FiscalQuarter = null);

public sealed record FinancialLineItemDto(
    string Code,
    string Name,
    decimal Amount,
    string? Unit);

public sealed record FinancialDataResult(
    string Ticker,
    string StatementType,
    string PeriodType,
    int FiscalYear,
    int? FiscalQuarter,
    DateOnly PeriodEndDate,
    string Currency,
    IReadOnlyList<FinancialLineItemDto> LineItems);

public interface IFinancialDataService
{
    Task<IReadOnlyList<FinancialDataResult>> QueryAsync(
        FinancialDataQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> SearchTickersAsync(
        string hint,
        CancellationToken cancellationToken = default);
}
