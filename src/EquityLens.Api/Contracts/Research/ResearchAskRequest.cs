namespace EquityLens.Api.Contracts.Research;

public sealed record ResearchAskRequest(
    string Ticker,
    string Question,
    RetrievalMode? RetrievalMode = null,
    string? DocumentType = null,
    SourcePolicy SourcePolicy = default,
    int TopK = 8,
    double Temperature = 0.2,
    bool Debug = false);
