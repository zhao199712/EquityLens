namespace EquityLens.Api.Services.Agents;

public sealed record LoadResearchRunNodeInput(Guid ResearchRunId);

public sealed record LoadResearchRunNodeOutput(
    string Ticker,
    string Status,
    int CitationCount,
    int CandidateCount,
    bool HasAnswer);

public sealed record BuildEvidencePacketNodeInput(
    string? Ticker,
    string? SourceStatus,
    int CitationCount,
    int CandidateCount,
    bool HasAnswer);

public sealed record BuildEvidencePacketNodeOutput(
    string? Ticker,
    string SourceStatus,
    int CitationCount,
    int CandidateCount,
    EvidencePacketSummary EvidenceSummary);

public sealed record EvidencePacket(
    string? Ticker,
    string? Question,
    string? Answer,
    string SourceStatus,
    int CitationCount,
    int CandidateCount,
    IReadOnlyList<EvidencePacketCitation> Citations,
    EvidencePacketSummary EvidenceSummary);

public sealed record EvidencePacketCitation(
    int CitationIndex,
    string? SourceType,
    Guid? DocumentId,
    Guid? DocumentChunkId,
    string? Title,
    string? DocumentType,
    int? PageNumber,
    string? QuoteText);

public sealed record EvidencePacketSummary(
    bool HasAnswer,
    bool HasCitations,
    int EmptyQuoteCount,
    int SelectedCandidateCount);

public sealed record CheckEvidenceNodeInput(
    string? Ticker,
    string SourceStatus,
    int CitationCount,
    int CandidateCount);

public sealed record CheckEvidenceNodeOutput(
    int CitationCount,
    int CandidateCount,
    string SourceStatus,
    int FindingCount,
    IReadOnlyList<CriticFinding> Findings);

public sealed record FinalizeCriticReportNodeInput(
    string OverallSeverity,
    bool RequiresRevision,
    bool RequiresMoreEvidence,
    string? RouteBackTo,
    string RecommendedNextAction);

public sealed record FinalizeCriticReportNodeOutput(
    string Summary,
    string OverallSeverity,
    IReadOnlyList<CriticFinding> Findings,
    bool RequiresRevision,
    bool RequiresMoreEvidence,
    string? RouteBackTo,
    string RecommendedNextAction,
    string? SuggestedAnswerRevision);

public sealed record LoadCriticReviewRunNodeInput(Guid CriticReviewRunId);

public sealed record LoadCriticReviewRunNodeOutput(
    Guid CriticReviewRunId,
    string SourceWorkflowType,
    string SourceStatus,
    bool HasOutput,
    bool RequiresRevision);

public sealed record DraftRevisedAnswerNodeInput(
    string? Ticker,
    string? Question,
    string? SourceAnswer,
    bool RequiresRevision,
    string? SuggestedAnswerRevision);

public sealed record DraftRevisedAnswerNodeOutput(
    string? SourceAnswer,
    string RevisedAnswer,
    string RevisionSummary,
    bool RevisionRequired,
    string? AppliedRecommendation);

public sealed record FinalizeRevisionNodeInput(
    bool RevisionRequired,
    string RevisionSummary);

public sealed record FinalizeRevisionNodeOutput(
    string? SourceAnswer,
    string RevisedAnswer,
    string RevisionSummary,
    bool RevisionRequired,
    string? AppliedRecommendation);
