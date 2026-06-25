using System.Diagnostics;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Observability;
using EquityLens.Api.Services.Ai.Retrieval;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Services.Ai;

public sealed class ResearchAnswerService : IResearchAnswerService
{
    private const string InsufficientEvidenceAnswer = "目前提供的資料不足以回答此問題。";
    private readonly IIntentDetector _intentDetector;
    private readonly IRetrievalPlanner _retrievalPlanner;
    private readonly IDocumentRetriever _documentRetriever;
    private readonly IWebRetriever _webRetriever;
    private readonly IResultReranker _resultReranker;
    private readonly IContextSelector _contextSelector;
    private readonly IContextFormatter _contextFormatter;
    private readonly IChunkContentCleaner _contentCleaner;
    private readonly IAnswerGenerator _answerGenerator;
    private readonly RetrievalOptions _options;
    private readonly ILogger<ResearchAnswerService> _logger;

    public ResearchAnswerService(
        IIntentDetector intentDetector,
        IRetrievalPlanner retrievalPlanner,
        IDocumentRetriever documentRetriever,
        IWebRetriever webRetriever,
        IResultReranker resultReranker,
        IContextSelector contextSelector,
        IContextFormatter contextFormatter,
        IChunkContentCleaner contentCleaner,
        IAnswerGenerator answerGenerator,
        IOptions<RetrievalOptions> options,
        ILogger<ResearchAnswerService> logger)
    {
        _intentDetector = intentDetector;
        _retrievalPlanner = retrievalPlanner;
        _documentRetriever = documentRetriever;
        _webRetriever = webRetriever;
        _resultReranker = resultReranker;
        _contextSelector = contextSelector;
        _contextFormatter = contextFormatter;
        _contentCleaner = contentCleaner;
        _answerGenerator = answerGenerator;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ResearchAskResponse> AskAsync(ResearchAskRequest request, CancellationToken cancellationToken = default)
    {
        using var askActivity = EquityLensTelemetry.ActivitySource.StartActivity("research.ask");
        var totalStopwatch = Stopwatch.StartNew();
        var outcome = "error";

        try
        {
            var searchStopwatch = Stopwatch.StartNew();
            var topK = Math.Clamp(request.TopK <= 0 ? _options.DefaultTopK : request.TopK, 1, _options.MaxTopK);

            IntentDetectionResult intentDetection;
            using (var intentActivity = EquityLensTelemetry.ActivitySource.StartActivity("intent.detect"))
            {
                intentDetection = _intentDetector.Detect(request.Question);
                intentActivity?.SetTag("intent", intentDetection.Selected.ToString());
                intentActivity?.SetStatus(ActivityStatusCode.Ok);
            }

            ResearchRetrievalStrategy strategy;
            using (var planActivity = EquityLensTelemetry.ActivitySource.StartActivity("retrieval.plan"))
            {
                strategy = _retrievalPlanner.BuildPlan(request.Question, request.RetrievalMode, request.DocumentType, topK);
                planActivity?.SetTag("retrieval.mode", strategy.Mode);
                planActivity?.SetTag("retrieval.search_count", strategy.Searches.Count);
                planActivity?.SetStatus(ActivityStatusCode.Ok);
            }

            var chunks = new List<RetrievedDocumentChunk>();
            var webChunks = new List<RetrievedDocumentChunk>();
            var shouldSearchWeb = request.SourcePolicy is SourcePolicy.WebOnly or SourcePolicy.LocalAndWeb;

            if (request.SourcePolicy is SourcePolicy.LocalOnly or SourcePolicy.LocalThenWeb or SourcePolicy.LocalAndWeb)
            {
                var localChunks = await _documentRetriever.RetrieveAsync(strategy, request.Ticker, cancellationToken);
                chunks.AddRange(localChunks);
            }

            if (shouldSearchWeb)
            {
                var braveResults = await _webRetriever.RetrieveWebAsync(
                    request.Question,
                    _options.WebSearchCandidateCount,
                    _options.WebSearchFreshness,
                    cancellationToken);
                webChunks.AddRange(braveResults);
            }

            searchStopwatch.Stop();

            LogCandidateThreshold(chunks);

            var rerankStopwatch = Stopwatch.StartNew();
            var rankedLocal = await RankLocalBySearchPathAsync(chunks, strategy, intentDetection.Selected, request.Question, topK);

            if (request.SourcePolicy == SourcePolicy.LocalThenWeb && rankedLocal.SelectedResults.Count == 0 && webChunks.Count == 0)
            {
                webChunks.AddRange(await _webRetriever.RetrieveWebAsync(
                    request.Question,
                    _options.WebSearchCandidateCount,
                    _options.WebSearchFreshness,
                    cancellationToken));
            }

            IReadOnlyList<RetrievedDocumentChunk> selectedWeb = [];
            if (webChunks.Count > 0)
            {
                var rerankedWeb = await _resultReranker.Rank(webChunks, intentDetection.Selected, _options.WebContextLimit);
                selectedWeb = rerankedWeb.SelectedResults;
            }

            var mergedChunks = MergeLocalAndWeb(rankedLocal.SelectedResults, selectedWeb, topK);
            var mergedDecisions = rankedLocal.Decisions
                .Concat(selectedWeb.Select(w => new RankingDecision(
                    w, null, null, null, null, "Selected", "Web search result selected into context.", null)))
                .ToList();

            rerankStopwatch.Stop();

            if (mergedChunks.Count == 0)
            {
                return await BuildInsufficientEvidenceResponseAsync(
                    request,
                    intentDetection,
                    strategy,
                    new RankedSelection([], mergedDecisions),
                    searchStopwatch.ElapsedMilliseconds,
                    rerankStopwatch.ElapsedMilliseconds,
                    totalStopwatch,
                    askActivity);
            }

            var selectedChunks = mergedChunks.Select((chunk, index) => new SelectedChunk(chunk, index + 1)).ToList();
            var contextText = _contextFormatter.Format(selectedChunks);

            var generationStopwatch = Stopwatch.StartNew();
            var answerResult = await _answerGenerator.GenerateAsync(
                request.Question,
                contextText,
                null,
                request.Temperature,
                selectedChunks.Count,
                cancellationToken);
            generationStopwatch.Stop();

            var now = DateTimeOffset.UtcNow;
            var citations = selectedChunks
                .Select(selected => new ResearchCitation(
                    Index: selected.Index,
                    SourceType: selected.Chunk.SourceType,
                    DocumentChunkId: selected.Chunk.SourceType == CitationSourceType.LocalDocument ? selected.Chunk.Result.DocumentChunkId : null,
                    DocumentId: selected.Chunk.SourceType == CitationSourceType.LocalDocument ? selected.Chunk.Result.DocumentId : null,
                    Title: selected.Chunk.Result.DocumentTitle,
                    DocumentType: selected.Chunk.Result.DocumentType,
                    SourceRole: selected.Chunk.SourceRole,
                    PageNumber: selected.Chunk.Result.PageNumber,
                    Url: selected.Chunk.Url,
                    PublishedAt: selected.Chunk.PublishedAt,
                    RetrievedAt: selected.Chunk.RetrievedAt ?? now,
                    QuoteText: BuildQuoteText(selected.Chunk.Result.Content),
                    RelevanceScore: selected.Chunk.Result.RelevanceScore))
                .ToList();

            totalStopwatch.Stop();

            var rankingDecisions = mergedDecisions;
            var trace = request.Debug
                ? BuildTrace(
                    intentDetection,
                    strategy,
                    rankingDecisions,
                    answerResult,
                    searchStopwatch.ElapsedMilliseconds,
                    rerankStopwatch.ElapsedMilliseconds,
                    generationStopwatch.ElapsedMilliseconds,
                    totalStopwatch.ElapsedMilliseconds,
                    null)
                : null;

            var allDecisions = rankingDecisions.Count;
            var selectedCount = rankingDecisions.Count(d => d.Decision == "Selected");
            askActivity?.SetTag("intent", intentDetection.Selected.ToString());
            askActivity?.SetTag("retrieval.mode", strategy.Mode);
            askActivity?.SetTag("source_policy", request.SourcePolicy.ToString());
            askActivity?.SetTag("candidate.count", allDecisions);
            askActivity?.SetTag("selected.count", selectedCount);
            askActivity?.SetTag("llm.provider", _answerGenerator.Provider);
            askActivity?.SetTag("llm.model", answerResult.Model);
            askActivity?.SetStatus(ActivityStatusCode.Ok);
            outcome = "success";

            _logger.LogInformation(
                "Research ask completed with {CandidateCount} candidates, {SelectedCount} selected, source policy {SourcePolicy}, model {Model}, duration {DurationMs} ms",
                allDecisions,
                selectedCount,
                request.SourcePolicy,
                answerResult.Model,
                totalStopwatch.ElapsedMilliseconds);

            return new ResearchAskResponse(
                Question: request.Question,
                Answer: answerResult.Answer,
                Model: answerResult.Model,
                RetrievalStrategy: strategy,
                Citations: citations,
                Trace: trace,
                Status: answerResult.CitationValidationFailed ? "CitationValidationFailed" : "Answered");
        }
        catch (Exception exception)
        {
            EquityLensTelemetry.MarkError(askActivity, exception);
            _logger.LogError(exception, "Research ask failed with {ErrorType}", exception.GetType().Name);
            throw;
        }
        finally
        {
            totalStopwatch.Stop();
            var tags = new TagList { { "outcome", outcome } };
            EquityLensTelemetry.AskRequests.Add(1, tags);
            EquityLensTelemetry.AskDuration.Record(totalStopwatch.Elapsed.TotalMilliseconds, tags);
        }
    }

    private string BuildQuoteText(string content)
    {
        var cleaned = _contentCleaner.Clean(content);
        return cleaned.Length <= 300 ? cleaned : cleaned[..300] + "...";
    }

    private async Task<RankedSelection> RankLocalBySearchPathAsync(
        IReadOnlyList<RetrievedDocumentChunk> chunks,
        ResearchRetrievalStrategy strategy,
        ResearchQuestionIntent intent,
        string question,
        int topK)
    {
        if (chunks.Count == 0)
        {
            return await _resultReranker.Rank(chunks, intent, topK);
        }

        var selected = new List<RetrievedDocumentChunk>();
        var decisions = new List<RankingDecision>();
        var processedChunkIds = new HashSet<Guid>();
        var consumedChunkIds = new HashSet<Guid>();
        var consumedExactContent = new HashSet<string>(StringComparer.Ordinal);

        for (var searchIndex = 0; searchIndex < strategy.Searches.Count; searchIndex++)
        {
            var search = strategy.Searches[searchIndex];
            var searchId = $"search-{searchIndex + 1}";
            var searchChunks = chunks
                .Where(chunk => string.Equals(chunk.SearchId, searchId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var chunk in searchChunks)
            {
                processedChunkIds.Add(chunk.Result.DocumentChunkId);
            }

            if (searchChunks.Count == 0)
            {
                continue;
            }

            var routeTopK = Math.Min(search.TopK, topK);
            var routeCandidateLimit = Math.Min(searchChunks.Count, Math.Max(routeTopK * 2, routeTopK + 4));
            var ranked = await _resultReranker.Rank(searchChunks, intent, routeCandidateLimit);
            decisions.AddRange(ranked.Decisions);
            var routeSelected = DeduplicatePresentationLanguages(ranked.SelectedResults, decisions, question, routeTopK);

            foreach (var chunk in routeSelected)
            {
                if (selected.Count < topK
                    && consumedChunkIds.Add(chunk.Result.DocumentChunkId)
                    && consumedExactContent.Add(chunk.Result.Content))
                {
                    selected.Add(chunk);
                }
            }
        }

        var unplannedChunks = chunks
            .Where(chunk => !processedChunkIds.Contains(chunk.Result.DocumentChunkId))
            .ToList();
        if (selected.Count < topK && unplannedChunks.Count > 0)
        {
            var fallback = await _resultReranker.Rank(unplannedChunks, intent, topK - selected.Count);
            decisions.AddRange(fallback.Decisions);

            foreach (var chunk in fallback.SelectedResults)
            {
                if (selected.Count == topK)
                {
                    break;
                }

                if (consumedChunkIds.Add(chunk.Result.DocumentChunkId)
                    && consumedExactContent.Add(chunk.Result.Content))
                {
                    selected.Add(chunk);
                }
            }
        }

        return new RankedSelection(selected, decisions);
    }

    private static IReadOnlyList<RetrievedDocumentChunk> DeduplicatePresentationLanguages(
        IReadOnlyList<RetrievedDocumentChunk> chunks,
        List<RankingDecision> decisions,
        string question,
        int topK)
    {
        var preferredLanguage = DetectPreferredLanguage(question);
        var selected = new List<RetrievedDocumentChunk>();
        var selectedKeys = new Dictionary<string, RetrievedDocumentChunk>(StringComparer.OrdinalIgnoreCase);

        foreach (var chunk in chunks)
        {
            var key = BuildPresentationPageKey(chunk);
            if (key is null)
            {
                if (selected.Count < topK)
                {
                    selected.Add(chunk);
                }
                continue;
            }

            if (!selectedKeys.TryGetValue(key, out var existing))
            {
                if (selected.Count < topK)
                {
                    selected.Add(chunk);
                    selectedKeys[key] = chunk;
                }
                continue;
            }

            if (!ShouldPreferPresentationChunk(chunk, existing, preferredLanguage))
            {
                decisions.Add(BuildLanguageDedupDecision(chunk, existing));
                continue;
            }

            var index = selected.FindIndex(item => item.Result.DocumentChunkId == existing.Result.DocumentChunkId);
            if (index >= 0)
            {
                selected[index] = chunk;
                selectedKeys[key] = chunk;
                decisions.Add(BuildLanguageDedupDecision(existing, chunk));
            }
            else
            {
                decisions.Add(BuildLanguageDedupDecision(chunk, existing));
            }
        }

        foreach (var chunk in chunks)
        {
            if (selected.Count == topK)
            {
                break;
            }

            if (selected.Any(item => item.Result.DocumentChunkId == chunk.Result.DocumentChunkId))
            {
                continue;
            }

            var key = BuildPresentationPageKey(chunk);
            if (key is not null && selectedKeys.ContainsKey(key))
            {
                decisions.Add(BuildLanguageDedupDecision(chunk, selectedKeys[key]));
                continue;
            }

            selected.Add(chunk);
            if (key is not null)
            {
                selectedKeys[key] = chunk;
            }
        }

        return selected;
    }

    private static RankingDecision BuildLanguageDedupDecision(
        RetrievedDocumentChunk discarded,
        RetrievedDocumentChunk kept)
    {
        return new RankingDecision(
            discarded,
            null,
            null,
            null,
            null,
            "DiscardedByPresentationLanguageDedup",
            "Presentation page was removed because another language version of the same page was preferred.",
            kept.Result.DocumentChunkId);
    }

    private static string DetectPreferredLanguage(string question)
    {
        return question.Any(c => c >= '\u4e00' && c <= '\u9fff') ? "zh-TW" : "en";
    }

    private static bool ShouldPreferPresentationChunk(
        RetrievedDocumentChunk candidate,
        RetrievedDocumentChunk existing,
        string preferredLanguage)
    {
        var candidateLanguage = InferPresentationLanguage(candidate);
        var existingLanguage = InferPresentationLanguage(existing);
        var candidatePreferred = string.Equals(candidateLanguage, preferredLanguage, StringComparison.OrdinalIgnoreCase);
        var existingPreferred = string.Equals(existingLanguage, preferredLanguage, StringComparison.OrdinalIgnoreCase);

        if (candidatePreferred != existingPreferred)
        {
            return candidatePreferred;
        }

        return candidate.Result.RelevanceScore > existing.Result.RelevanceScore;
    }

    private static string? BuildPresentationPageKey(RetrievedDocumentChunk chunk)
    {
        var result = chunk.Result;
        if (!string.Equals(result.DocumentType, "EarningsPresentation", StringComparison.OrdinalIgnoreCase)
            || result.PageNumber is null)
        {
            return null;
        }

        return $"{result.Ticker ?? string.Empty}:{NormalizePresentationFamily(result.DocumentTitle)}:{result.PageNumber.Value}";
    }

    private static string NormalizePresentationFamily(string title)
    {
        return title
            .Replace("E001", "001", StringComparison.OrdinalIgnoreCase)
            .Replace("M001", "001", StringComparison.OrdinalIgnoreCase)
            .Replace("_en", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("_zh", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("-en", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("-zh", string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static string InferPresentationLanguage(RetrievedDocumentChunk chunk)
    {
        var title = chunk.Result.DocumentTitle;
        if (title.Contains("M001", StringComparison.OrdinalIgnoreCase))
        {
            return "zh-TW";
        }

        if (title.Contains("E001", StringComparison.OrdinalIgnoreCase))
        {
            return "en";
        }

        var content = chunk.Result.Content;
        if (content.Contains("Language: zh", StringComparison.OrdinalIgnoreCase))
        {
            return "zh-TW";
        }

        if (content.Contains("Language: en", StringComparison.OrdinalIgnoreCase))
        {
            return "en";
        }

        return "unknown";
    }

    private static IReadOnlyList<RetrievedDocumentChunk> MergeLocalAndWeb(
        IReadOnlyList<RetrievedDocumentChunk> local,
        IReadOnlyList<RetrievedDocumentChunk> web,
        int topK)
    {
        if (web.Count == 0) return local;
        if (local.Count == 0) return web;

        var merged = new List<RetrievedDocumentChunk>();
        merged.AddRange(local);
        merged.AddRange(web);
        return merged;
    }

    private void LogCandidateThreshold(IReadOnlyList<RetrievedDocumentChunk> chunks)
    {
        var belowCandidateThreshold = chunks.Count(chunk => chunk.Result.RelevanceScore < _options.MinimumCandidateScore);
        if (belowCandidateThreshold > 0)
        {
            _logger.LogInformation(
                "{BelowThresholdCount} of {CandidateCount} candidates below MinimumCandidateScore {MinimumCandidateScore}; logged only",
                belowCandidateThreshold,
                chunks.Count,
                _options.MinimumCandidateScore);
        }
    }

    private async Task<ResearchAskResponse> BuildInsufficientEvidenceResponseAsync(
        ResearchAskRequest request,
        IntentDetectionResult intentDetection,
        ResearchRetrievalStrategy strategy,
        RankedSelection rankedSelection,
        long searchMs,
        long rerankMs,
        Stopwatch totalStopwatch,
        Activity? askActivity)
    {
        await Task.CompletedTask;
        totalStopwatch.Stop();

        askActivity?.SetTag("intent", intentDetection.Selected.ToString());
        askActivity?.SetTag("retrieval.mode", strategy.Mode);
        askActivity?.SetTag("candidate.count", rankedSelection.Decisions.Count);
        askActivity?.SetTag("selected.count", 0);
        askActivity?.SetStatus(ActivityStatusCode.Ok);

        _logger.LogInformation(
            "Insufficient evidence for ticker {Ticker}, mode {RetrievalMode}, candidates {Count}",
            request.Ticker.Trim().ToUpperInvariant(),
            strategy.Mode,
            rankedSelection.Decisions.Count);

        var trace = request.Debug
            ? BuildTrace(
                intentDetection,
                strategy,
                rankedSelection.Decisions,
                new AnswerGenerationResult(string.Empty, string.Empty, 0, 0, 0, false, []),
                searchMs,
                rerankMs,
                0,
                totalStopwatch.ElapsedMilliseconds,
                null)
            : null;

        return new ResearchAskResponse(
            Question: request.Question,
            Answer: InsufficientEvidenceAnswer,
            Model: string.Empty,
            RetrievalStrategy: strategy,
            Citations: [],
            Trace: trace,
            Status: "InsufficientEvidence");
    }

    private static ResearchTrace BuildTrace(
        IntentDetectionResult intent,
        ResearchRetrievalStrategy strategy,
        IReadOnlyList<RankingDecision> decisions,
        AnswerGenerationResult answerResult,
        long searchMs,
        long rerankMs,
        long generationMs,
        long totalMs,
        string? retrievalNote)
    {
        var candidateCount = decisions.Count;
        var selectedCount = decisions.Count(d => d.Decision == "Selected");
        var discardedByReason = decisions
            .Where(d => d.Decision != "Selected")
            .GroupBy(d => d.Decision)
            .ToDictionary(g => g.Key, g => g.Count());

        var traceResults = decisions.Select(decision =>
        {
            var result = decision.Chunk.Result;
            return new ResearchTraceResult(
                SearchId: decision.Chunk.SearchId,
                Query: decision.Chunk.Query,
                DocumentChunkId: result.DocumentChunkId,
                DocumentId: result.DocumentId,
                DocumentTitle: result.DocumentTitle,
                DocumentType: result.DocumentType,
                SourceRole: decision.Chunk.SourceRole,
                PageNumber: result.PageNumber,
                RelevanceScore: result.RelevanceScore,
                AdjustedScore: decision.AdjustedScore,
                ScoreBreakdown: decision.ScoreBreakdown,
                RankBeforeRerank: decision.RankBeforeRerank,
                RankAfterRerank: decision.RankAfterRerank,
                Selected: decision.Decision == "Selected",
                Decision: decision.Decision,
                Reason: decision.Reason,
                DuplicateOfChunkId: decision.DuplicateOfChunkId,
                ContentPreview: BuildContentPreview(result.Content));
        }).ToList();

        return new ResearchTrace(
            TraceId: Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N"),
            Intent: new ResearchTraceIntent(intent.Selected.ToString(), intent.MatchedKeywords, intent.Confidence),
            RetrievalStrategy: strategy,
            Retrieval: new ResearchTraceRetrievalSummary(candidateCount, selectedCount, candidateCount - selectedCount, discardedByReason),
            TokenUsage: new ResearchTraceTokenUsage(answerResult.Model, answerResult.PromptTokens, answerResult.CompletionTokens, answerResult.PromptTokens + answerResult.CompletionTokens),
            Results: traceResults,
            LatencyMs: new ResearchTraceLatency(searchMs, rerankMs, generationMs, totalMs),
            RetrievalNote: retrievalNote);
    }

    private static string BuildContentPreview(string content)
    {
        const int maxLength = 300;
        var normalized = string.Join(" ", content
            .Replace("\n", " ")
            .Replace("\t", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength] + "...";
    }
}
