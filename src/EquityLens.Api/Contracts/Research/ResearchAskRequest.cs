namespace EquityLens.Api.Contracts.Research;

public sealed record ResearchAskRequest(
    string Ticker,
    string Question,
    string? RetrievalMode = null,
    string? DocumentType = null,
    int TopK = 8,
    double Temperature = 0.2,
    bool Debug = false);
