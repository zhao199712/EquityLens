namespace EquityLens.Api.Contracts.PortfolioDividends;

public sealed record LatestDividendResponse(
    Guid SecurityId,
    string Ticker,
    string SecurityName,
    DateOnly ExDividendDate,
    DateOnly? PaymentDate,
    decimal CashAmountPerShare,
    string Currency);
