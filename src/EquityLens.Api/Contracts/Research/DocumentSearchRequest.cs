namespace EquityLens.Api.Contracts.Research;

public sealed record DocumentSearchRequest(
    string Query,
    string? Ticker = null,
    string? DocumentType = null,
    int? TopK = null);
