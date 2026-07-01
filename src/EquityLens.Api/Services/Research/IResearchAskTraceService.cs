using EquityLens.Api.Contracts.Research;

namespace EquityLens.Api.Services.Research;

public interface IResearchAskTraceService
{
    Task<Guid> PersistAsync(
        ResearchAskRequest request,
        ResearchAskResponse response,
        CancellationToken cancellationToken = default);

    Task<ResearchAskTraceRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ResearchAskTraceSummary>> ListAsync(
        int limit = 50,
        string? ticker = null,
        string? status = null,
        Guid? userId = null,
        CancellationToken cancellationToken = default);
}

public sealed record ResearchAskTraceSummary(
    Guid Id,
    string Ticker,
    string Question,
    string Status,
    int CitationCount,
    string RetrievalMode,
    int TotalTokens,
    long TotalLatencyMs,
    DateTime CreatedAtUtc);

public sealed record ResearchAskTraceRecord(
    ResearchAskTraceSummary Summary,
    string Answer,
    string ActivityTraceId,
    string? Model,
    string SourcePolicy,
    int TopK,
    double Temperature,
    int CandidateCount,
    int SelectedCount,
    int DiscardedCount,
    int FinalCitationCount,
    int PromptTokens,
    int CompletionTokens,
    int SearchLatencyMs,
    int RerankLatencyMs,
    int GenerationLatencyMs,
    string RetrievalStrategyJson,
    string? TraceJson,
    IReadOnlyList<ResearchAskTraceCitationDto> Citations);

public sealed record ResearchAskTraceCitationDto(
    int CitationIndex,
    string SourceType,
    Guid? DocumentChunkId,
    Guid? DocumentId,
    string Title,
    string? DocumentType,
    string SourceRole,
    int? PageNumber,
    string? Url,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? RetrievedAt,
    string QuoteText,
    double RelevanceScore);
