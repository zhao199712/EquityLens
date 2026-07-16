namespace EquityLens.Api.Contracts.PortfolioTransactions;

public sealed record TransactionResponse(
    Guid Id,
    Guid SecurityId,
    string Ticker,
    string Exchange,
    string SecurityName,
    string TransactionType,
    decimal Quantity,
    decimal Price,
    decimal Fee,
    DateOnly TransactionDate,
    string? Note,
    DateTime CreatedAtUtc,
    decimal? NetProceeds = null,
    decimal? FifoCost = null,
    decimal? RealizedPnl = null);
