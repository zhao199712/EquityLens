namespace EquityLens.Api.Contracts.Research;

public sealed record DocumentSearchResponse(
    string Query,
    string EmbeddingModel,
    IReadOnlyList<DocumentSearchResult> Results);

public sealed record DocumentSearchResult(
    Guid DocumentChunkId,
    Guid DocumentId,
    string DocumentTitle,
    string? DocumentType,
    string? SourceUrl,
    int ChunkIndex,
    int? PageNumber,
    string? SectionTitle,
    string Content,
    double Distance,
    double RelevanceScore,
    Guid? SecurityId,
    string? Ticker,
    string? Exchange,
    string? SecurityName);
