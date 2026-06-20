namespace EquityLens.Api.Contracts.Research;

public sealed record ResearchAskResponse(
    string Question,
    string Answer,
    string Model,
    ResearchRetrievalStrategy RetrievalStrategy,
    IReadOnlyList<ResearchCitation> Citations,
    ResearchTrace? Trace = null);

public sealed record ResearchRetrievalStrategy(
    string Mode,
    IReadOnlyList<ResearchRetrievalSearch> Searches);

public sealed record ResearchRetrievalSearch(
    string? DocumentType,
    string SourceRole,
    string Query,
    int TopK,
    string Reason);

public sealed record ResearchCitation(
    int Index,
    Guid DocumentChunkId,
    Guid DocumentId,
    string DocumentTitle,
    string? DocumentType,
    string SourceRole,
    int? PageNumber,
    string QuoteText,
    double RelevanceScore);

public sealed record ResearchTrace(
    string Intent,
    ResearchRetrievalStrategy RetrievalStrategy,
    IReadOnlyList<ResearchTraceResult> Results,
    ResearchTraceLatency LatencyMs,
    string? RetrievalNote);

public sealed record ResearchTraceResult(
    Guid DocumentChunkId,
    Guid DocumentId,
    string DocumentTitle,
    string? DocumentType,
    string SourceRole,
    int? PageNumber,
    double RelevanceScore,
    double? AdjustedScore,
    bool Selected,
    string Decision,
    string Reason,
    string ContentPreview);

public sealed record ResearchTraceLatency(
    long Retrieval,
    long Generation,
    long Total);
