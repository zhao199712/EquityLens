namespace EquityLens.Api.Contracts.PortfolioDividends;

public sealed record DividendCashFlowResponse(
    Guid Id,
    Guid SecurityId,
    string Ticker,
    string SecurityName,
    DateOnly ExDividendDate,
    DateOnly? PaymentDate,
    decimal SharesEntitled,
    decimal CashAmountPerShare,
    decimal Amount,
    string Currency,
    string Status,
    bool IsUserAdjusted,
    string? Note);
