using EquityLens.Api.Contracts.Research;

namespace EquityLens.Api.Services.Research;

public sealed record StepInput(
    string StepType,
    string? InputJson,
    string? OutputJson,
    long? DurationMs,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? ErrorMessage);

public interface IResearchRunTraceService
{
    Task<Guid> PersistAskAsync(
        Guid userId,
        ResearchAskRequest request,
        ResearchAskResponse response,
        IReadOnlyList<StepInput>? steps = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ResearchRunSummaryDto>> ListAsync(
        Guid? userId,
        int limit = 50,
        string? ticker = null,
        string? status = null,
        CancellationToken cancellationToken = default);

    Task<ResearchRunDetailDto?> GetByIdAsync(
        Guid id,
        Guid? userId,
        CancellationToken cancellationToken = default);
}

public sealed record ResearchRunSummaryDto(
    Guid Id,
    string Ticker,
    string Question,
    string Status,
    int CitationCount,
    string RetrievalMode,
    long LatencyMs,
    DateTime CreatedAtUtc,
    Guid? ParentResearchRunId = null,
    Guid? RevisionFeedbackId = null,
    string SourcePolicy = "Auto",
    string? DocumentType = null,
    int TopK = 8,
    double Temperature = 0.2);

public sealed record ResearchRunStepDto(
    Guid Id,
    string StepType,
    string? InputJson,
    string? OutputJson,
    long? DurationMs,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? ErrorMessage);

public sealed record ResearchRunCandidateDto(
    Guid Id,
    Guid DocumentChunkId,
    Guid? DocumentId,
    string? Title,
    string? DocumentType,
    string? SourceRole,
    int? PageNumber,
    double RelevanceScore,
    double? AdjustedScore,
    int? RankBeforeRerank,
    int? RankAfterRerank,
    string Decision,
    string? DiscardReason,
    string? ContentPreview);

public sealed record ResearchRunCitationDto(
    Guid Id,
    int CitationIndex,
    string SourceType,
    Guid? DocumentChunkId,
    Guid? DocumentId,
    string? Title,
    string? DocumentType,
    string? SourceRole,
    int? PageNumber,
    string? QuoteText,
    double RelevanceScore);

public sealed record ResearchRunDetailDto(
    ResearchRunSummaryDto Run,
    string Answer,
    IReadOnlyList<ResearchRunStepDto> Steps,
    IReadOnlyList<ResearchRunCandidateDto> Candidates,
    IReadOnlyList<ResearchRunCitationDto> Citations);
