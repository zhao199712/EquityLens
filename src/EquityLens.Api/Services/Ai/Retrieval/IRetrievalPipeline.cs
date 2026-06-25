using EquityLens.Api.Contracts.Research;

namespace EquityLens.Api.Services.Ai.Retrieval;

public interface IIntentDetector
{
    IntentDetectionResult Detect(string question);
}

public interface IRetrievalPlanner
{
    ResearchRetrievalStrategy BuildPlan(
        string question,
        RetrievalMode? mode,
        string? documentType,
        int topK);
}

public interface IDocumentRetriever
{
    Task<IReadOnlyList<RetrievedDocumentChunk>> RetrieveAsync(
        ResearchRetrievalStrategy strategy,
        string ticker,
        CancellationToken cancellationToken = default);

    IReadOnlyList<RetrievedDocumentChunk> GetCandidatesForRerank(
        IReadOnlyList<RetrievedDocumentChunk> chunks,
        int targetCount);
}

public interface IWebRetriever
{
    Task<IReadOnlyList<RetrievedDocumentChunk>> RetrieveWebAsync(
        string query,
        int count,
        string? freshness,
        CancellationToken cancellationToken = default);
}

public interface IDocumentReranker
{
    Task<IReadOnlyList<RetrievedDocumentChunk>> RerankAsync(
        string query,
        IReadOnlyList<RetrievedDocumentChunk> chunks,
        int topN,
        CancellationToken cancellationToken = default);
}

public interface IChunkContentCleaner
{
    string Clean(string content);
}

public interface IResultReranker
{
    Task<RankedSelection> Rank(
        IReadOnlyList<RetrievedDocumentChunk> chunks,
        ResearchQuestionIntent intent,
        int topK);
}

public interface IContextSelector
{
    ContextSelection Select(
        RankedSelection ranked,
        ResearchQuestionIntent intent,
        ResearchRetrievalStrategy strategy);
}

public interface IContextFormatter
{
    string Format(IReadOnlyList<SelectedChunk> chunks);
}

public interface IAnswerGenerator
{
    string Provider { get; }
    string Model { get; }

    Task<AnswerGenerationResult> GenerateAsync(
        string question,
        string context,
        string? retrievalNote,
        double temperature,
        int maxValidCitationIndex,
        CancellationToken cancellationToken = default);
}

public interface ICitationValidator
{
    CitationValidationResult Validate(string answer, int maxValidIndex);
}
