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
    string TraceId,
    ResearchTraceIntent Intent,
    ResearchRetrievalStrategy RetrievalStrategy,
    ResearchTraceRetrievalSummary Retrieval,
    ResearchTraceTokenUsage TokenUsage,
    IReadOnlyList<ResearchTraceResult> Results,
    ResearchTraceLatency LatencyMs,
    string? RetrievalNote);

public sealed record ResearchTraceIntent(
    string Selected,
    IReadOnlyList<string> MatchedKeywords,
    double Confidence);

public sealed record ResearchTraceRetrievalSummary(
    int CandidateCount,
    int SelectedCount,
    int DiscardedCount,
    IReadOnlyDictionary<string, int> DiscardedByReason);

public sealed record ResearchTraceTokenUsage(
    string Model,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens);

public sealed record ResearchTraceResult(
    string SearchId,
    string Query,
    Guid DocumentChunkId,
    Guid DocumentId,
    string DocumentTitle,
    string? DocumentType,
    string SourceRole,
    int? PageNumber,
    double RelevanceScore,
    double? AdjustedScore,
    ResearchTraceScoreBreakdown? ScoreBreakdown,
    int? RankBeforeRerank,
    int? RankAfterRerank,
    bool Selected,
    string Decision,
    string Reason,
    Guid? DuplicateOfChunkId,
    string ContentPreview);

public sealed record ResearchTraceScoreBreakdown(
    double Embedding,
    double PrimaryBonus,
    double RiskEvidenceBonus,
    double FinancialEvidenceBonus,
    double OutlookEvidenceBonus,
    double AgendaPenalty,
    double SafeHarborPenalty,
    double FirstPagePenalty,
    double Final);

public sealed record ResearchTraceLatency(
    long Search,
    long Rerank,
    long Generation,
    long Total);
