namespace EquityLens.Api.Contracts.PortfolioTransactions;

public sealed record CreateTransactionRequest(
    Guid SecurityId,
    string TransactionType,
    decimal Quantity,
    decimal Price,
    decimal? Fee,
    DateOnly TransactionDate,
    string? Note);
