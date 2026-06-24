using EquityLens.Api.Contracts.Research;

namespace EquityLens.Api.Services.Ai.Retrieval;

public enum ResearchQuestionIntent
{
    Risk,
    Financial,
    Outlook,
    General
}

public sealed record IntentDetectionResult(
    ResearchQuestionIntent Selected,
    IReadOnlyList<string> MatchedKeywords,
    double Confidence);

public sealed record RetrievedDocumentChunk(
    DocumentSearchResult Result,
    string SourceRole,
    string SearchId,
    string Query,
    CitationSourceType SourceType = CitationSourceType.LocalDocument,
    string? Url = null,
    DateTimeOffset? PublishedAt = null,
    DateTimeOffset? RetrievedAt = null);

public sealed record RankedChunk(
    RetrievedDocumentChunk Chunk,
    ResearchTraceScoreBreakdown ScoreBreakdown,
    int RankBeforeRerank,
    int RankAfterRerank);

public sealed record RankingDecision(
    RetrievedDocumentChunk Chunk,
    double? AdjustedScore,
    ResearchTraceScoreBreakdown? ScoreBreakdown,
    int? RankBeforeRerank,
    int? RankAfterRerank,
    string Decision,
    string Reason,
    Guid? DuplicateOfChunkId);

public sealed record RankedSelection(
    IReadOnlyList<RetrievedDocumentChunk> SelectedResults,
    IReadOnlyList<RankingDecision> Decisions);

public sealed record SelectedChunk(
    RetrievedDocumentChunk Chunk,
    int Index);

public sealed record ContextSelection(
    IReadOnlyList<SelectedChunk> Chunks,
    string? RetrievalNote);

public sealed record AnswerGenerationResult(
    string Answer,
    string Model,
    int PromptTokens,
    int CompletionTokens,
    int RetryCount,
    bool CitationValidationFailed,
    IReadOnlyList<int> InvalidCitationIndices);

public sealed record CitationValidationResult(
    bool IsValid,
    IReadOnlyList<int> InvalidIndices,
    string SanitizedAnswer);
