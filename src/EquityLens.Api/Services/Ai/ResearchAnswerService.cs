using System.Diagnostics;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Observability;
using EquityLens.Api.Services.Ai.Retrieval;
using EquityLens.Api.Services.CurrentUser;
using EquityLens.Api.Services.Research;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Services.Ai;

public sealed class ResearchAnswerService : IResearchAnswerService
{
    private const string InsufficientEvidenceAnswer = "目前提供的資料不足以回答此問題。";
    private static readonly string[] InsufficientEvidencePatterns =
    [
        "資料不足以回答",
        "無法回答",
        "資料中沒有",
        "文件中沒有",
        "文件均未提及",
        "not enough information",
        "cannot answer",
        "do not have enough information"
    ];
    private static readonly char[] SentenceSeparators = ['。', '！', '？', '\n', '；'];
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
    private readonly IResearchRunTraceService _traceService;
    private readonly ICurrentUserContext _currentUser;
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
        IResearchRunTraceService traceService,
        ICurrentUserContext currentUser,
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
        _traceService = traceService;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<ResearchAskResponse> AskAsync(ResearchAskRequest request, CancellationToken cancellationToken = default)
    {
        using var askActivity = EquityLensTelemetry.ActivitySource.StartActivity("research.ask");
        var totalStopwatch = Stopwatch.StartNew();
        var outcome = "error";
        var steps = new List<StepInput>();

        try
        {
            var searchStopwatch = Stopwatch.StartNew();
            var topK = Math.Clamp(request.TopK <= 0 ? _options.DefaultTopK : request.TopK, 1, _options.MaxTopK);
            var temperature = Math.Clamp(request.Temperature, 0.0, 1.0);

            IntentDetectionResult intentDetection;
            var intentStart = DateTime.UtcNow;
            using (var intentActivity = EquityLensTelemetry.ActivitySource.StartActivity("intent.detect"))
            {
                intentDetection = _intentDetector.Detect(request.Question);
                intentActivity?.SetTag("intent", intentDetection.Selected.ToString());
                intentActivity?.SetStatus(ActivityStatusCode.Ok);
            }
            steps.Add(new StepInput("IntentDetection",
                $"{{\"questionLength\":{request.Question.Length}}}",
                $"{{\"intent\":\"{intentDetection.Selected}\",\"confidence\":{intentDetection.Confidence}}}",
                (long)(DateTime.UtcNow - intentStart).TotalMilliseconds,
                intentStart, DateTime.UtcNow, null));

            ResearchRetrievalStrategy strategy;
            var planStart = DateTime.UtcNow;
            using (var planActivity = EquityLensTelemetry.ActivitySource.StartActivity("retrieval.plan"))
            {
                strategy = _retrievalPlanner.BuildPlan(request.Question, request.RetrievalMode, request.DocumentType, topK);
                planActivity?.SetTag("retrieval.mode", strategy.Mode);
                planActivity?.SetTag("retrieval.search_count", strategy.Searches.Count);
                planActivity?.SetStatus(ActivityStatusCode.Ok);
            }
            steps.Add(new StepInput("RetrievalPlanning",
                $"{{\"mode\":\"{request.RetrievalMode}\",\"documentType\":\"{request.DocumentType}\",\"topK\":{topK}}}",
                $"{{\"mode\":\"{strategy.Mode}\",\"searchCount\":{strategy.Searches.Count}}}",
                (long)(DateTime.UtcNow - planStart).TotalMilliseconds,
                planStart, DateTime.UtcNow, null));

            var chunks = new List<RetrievedDocumentChunk>();
            var webChunks = new List<RetrievedDocumentChunk>();
            var shouldSearchWeb = request.SourcePolicy is SourcePolicy.WebOnly or SourcePolicy.LocalAndWeb
                || request.SourcePolicy == SourcePolicy.Auto && Services.Agents.ResearchInvestigationPlanning.IsFreshnessSensitive(request.Question);

            var localRetrievalStart = DateTime.UtcNow;
            if (request.SourcePolicy is SourcePolicy.Auto or SourcePolicy.LocalOnly or SourcePolicy.LocalThenWeb or SourcePolicy.LocalAndWeb)
            {
                var localChunks = await _documentRetriever.RetrieveAsync(strategy, request.Ticker, cancellationToken);
                chunks.AddRange(localChunks);
            }
            steps.Add(new StepInput("LocalRetrieval",
                $"{{\"ticker\":\"{request.Ticker}\",\"searches\":{strategy.Searches.Count}}}",
                $"{{\"candidateCount\":{chunks.Count}}}",
                (long)(DateTime.UtcNow - localRetrievalStart).TotalMilliseconds,
                localRetrievalStart, DateTime.UtcNow, null));

            var webRetrievalStart = DateTime.UtcNow;
            if (shouldSearchWeb)
            {
                var braveResults = await _webRetriever.RetrieveWebAsync(
                    request.Question,
                    _options.WebSearchCandidateCount,
                    _options.WebSearchFreshness,
                    cancellationToken);
                webChunks.AddRange(braveResults);
            }
            if (shouldSearchWeb)
            {
                steps.Add(new StepInput("WebRetrieval",
                    $"{{\"candidateCount\":{_options.WebSearchCandidateCount}}}",
                    $"{{\"resultCount\":{webChunks.Count}}}",
                    (long)(DateTime.UtcNow - webRetrievalStart).TotalMilliseconds,
                    webRetrievalStart, DateTime.UtcNow, null));
            }

            searchStopwatch.Stop();

            var candidateGateDecisions = new List<RankingDecision>();
            var eligibleChunks = ApplyCandidateScoreGate(chunks, candidateGateDecisions);

            var rerankStopwatch = Stopwatch.StartNew();
            var rankStart = DateTime.UtcNow;
            var rankedLocal = await RankLocalBySearchPathAsync(eligibleChunks, strategy, intentDetection.Selected, request.Question, topK);

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
            var mergedDecisions = candidateGateDecisions
                .Concat(rankedLocal.Decisions)
                .Concat(selectedWeb.Select(w => new RankingDecision(
                    w, null, null, null, null, "Selected", "Web search result selected into context.", null)))
                .ToList();

            rerankStopwatch.Stop();
            steps.Add(new StepInput("Rerank",
                $"{{\"candidateCount\":{eligibleChunks.Count},\"webCandidateCount\":{webChunks.Count},\"topK\":{topK}}}",
                $"{{\"selectedCount\":{rankedLocal.SelectedResults.Count},\"mergedCount\":{mergedChunks.Count}}}",
                rerankStopwatch.ElapsedMilliseconds,
                rankStart, DateTime.UtcNow, null));

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
                    askActivity,
                    steps);
            }

            var contextSelection = _contextSelector.Select(
                new RankedSelection(mergedChunks, mergedDecisions),
                intentDetection.Selected,
                strategy);
            var selectedChunks = contextSelection.Chunks;
            var contextText = _contextFormatter.Format(selectedChunks);

            var generationStopwatch = Stopwatch.StartNew();
            var genStart = DateTime.UtcNow;
            var answerResult = await _answerGenerator.GenerateAsync(
                request.Question,
                contextText,
                contextSelection.RetrievalNote,
                temperature,
                selectedChunks.Count,
                cancellationToken: cancellationToken);
            generationStopwatch.Stop();

            var hasCitationIssue = answerResult.CitationValidationFailed || answerResult.RetryCount > 0;
            steps.Add(new StepInput("AnswerGeneration",
                $"{{\"chunkCount\":{selectedChunks.Count},\"temperature\":{temperature},\"hasRetrievalNote\":{contextSelection.RetrievalNote is not null}}}",
                $"{{\"model\":\"{answerResult.Model}\",\"promptTokens\":{answerResult.PromptTokens},\"completionTokens\":{answerResult.CompletionTokens},\"retryCount\":{answerResult.RetryCount},\"citationFailed\":{answerResult.CitationValidationFailed}}}",
                generationStopwatch.ElapsedMilliseconds,
                genStart, DateTime.UtcNow, null));

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
                    QuoteText: BuildRelevantQuoteText(selected.Chunk.Result.Content, request.Question, intentDetection.Selected),
                    RelevanceScore: selected.Chunk.Result.RelevanceScore))
                .ToList();

            totalStopwatch.Stop();

            var status = DetermineAnswerStatus(answerResult.Answer, answerResult.CitationValidationFailed);
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
                    contextSelection.RetrievalNote,
                    finalCitationCount: citations.Count)
                : null;

            var allDecisions = rankingDecisions.Count;
            var selectedCount = rankingDecisions.Count(d => d.Decision == "Selected");
            askActivity?.SetTag("intent", intentDetection.Selected.ToString());
            askActivity?.SetTag("retrieval.mode", strategy.Mode);
            askActivity?.SetTag("source_policy", request.SourcePolicy.ToString());
            askActivity?.SetTag("candidate.count", allDecisions);
            askActivity?.SetTag("selected.count", selectedCount);
            askActivity?.SetTag("final.citation_count", citations.Count);
            askActivity?.SetTag("llm.provider", _answerGenerator.Provider);
            askActivity?.SetTag("llm.model", answerResult.Model);
            askActivity?.SetStatus(ActivityStatusCode.Ok);
            outcome = $"success_{status}";

            _logger.LogInformation(
                "Research ask completed with {CandidateCount} candidates, {SelectedCount} selected, {FinalCount} citations, source policy {SourcePolicy}, model {Model}, status {Status}, duration {DurationMs} ms",
                allDecisions,
                selectedCount,
                citations.Count,
                request.SourcePolicy,
                answerResult.Model,
                status,
                totalStopwatch.ElapsedMilliseconds);

            var response = new ResearchAskResponse(
                Question: request.Question,
                Answer: answerResult.Answer,
                Model: answerResult.Model,
                RetrievalStrategy: strategy,
                Citations: citations,
                Trace: trace,
                Status: status);

            return await PersistTraceAndReturnAsync(request, response, cancellationToken, askActivity, steps);
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

    private string BuildRelevantQuoteText(string content, string question, ResearchQuestionIntent intent)
    {
        var cleaned = _contentCleaner.Clean(content);
        var sentences = SplitSentences(cleaned);
        if (sentences.Count <= 2)
        {
            return cleaned.Length <= 300 ? cleaned : cleaned[..300] + "...";
        }

        var relevanceKeywords = GetRelevanceKeywords(question, intent);
        var scoredSentences = sentences
            .Select((s, i) => (Text: s, Index: i, Score: relevanceKeywords.Count(k => s.Contains(k, StringComparison.OrdinalIgnoreCase))))
            .ToList();

        var maxScore = scoredSentences.Max(s => s.Score);
        if (maxScore == 0)
        {
            return cleaned.Length <= 300 ? cleaned : cleaned[..300] + "...";
        }

        var high = scoredSentences
            .Where(s => s.Score == maxScore)
            .OrderBy(s => s.Index)
            .ToList();

        var result = string.Join("", high.Select(s => s.Text));
        if (result.Length < 80 && high.Count >= 1)
        {
            var spanStart = Math.Max(0, high[0].Index - 1);
            var spanEnd = Math.Min(sentences.Count - 1, high[^1].Index + 1);
            result = string.Join("", sentences.Skip(spanStart).Take(spanEnd - spanStart + 1));
        }

        result = result.Trim();
        return result.Length <= 300 ? result : result[..300] + "...";
    }

    private static List<string> SplitSentences(string text)
    {
        var sentences = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (SentenceSeparators.Contains(text[i]))
            {
                var sentence = text[start..(i + 1)].Trim();
                if (sentence.Length > 0)
                {
                    sentences.Add(sentence);
                }
                start = i + 1;
            }
        }
        if (start < text.Length)
        {
            var remaining = text[start..].Trim();
            if (remaining.Length > 0)
            {
                sentences.Add(remaining);
            }
        }
        return sentences;
    }

    private static IReadOnlyList<string> GetRelevanceKeywords(string question, ResearchQuestionIntent intent)
    {
        var keywords = new List<string>();
        var normalized = question.ToLowerInvariant();
        if (normalized.Contains("風險") || normalized.Contains("risk"))
            keywords.AddRange(["風險", "risk", "不確定", "uncertainty", "challenge", "headwind"]);
        if (normalized.Contains("營收") || normalized.Contains("revenue"))
            keywords.AddRange(["營收", "revenue", "收入", "毛利", "gross margin"]);
        if (normalized.Contains("毛利") || normalized.Contains("margin"))
            keywords.AddRange(["毛利率", "margin", "毛利"]);
        if (normalized.Contains("現金") || normalized.Contains("cash"))
            keywords.AddRange(["現金", "cash", "capital", "資本"]);
        if (normalized.Contains("展望") || normalized.Contains("outlook") || normalized.Contains("guidance"))
            keywords.AddRange(["展望", "outlook", "guidance", "預期", "expect"]);
        if (normalized.Contains("匯率") || normalized.Contains("外幣") || normalized.Contains("foreign exchange"))
            keywords.AddRange(["匯率", "外幣", "foreign exchange", "FX"]);
        if (keywords.Count == 0)
        {
            switch (intent)
            {
                case ResearchQuestionIntent.Risk:
                    keywords.AddRange(["風險", "risk", "不確定", "challenge"]);
                    break;
                case ResearchQuestionIntent.Financial:
                    keywords.AddRange(["營收", "revenue", "毛利", "margin", "現金流", "cash flow"]);
                    break;
                case ResearchQuestionIntent.Outlook:
                    keywords.AddRange(["展望", "outlook", "guidance", "預期"]);
                    break;
            }
        }
        return keywords.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string DetermineAnswerStatus(string answer, bool citationValidationFailed)
    {
        if (citationValidationFailed)
        {
            return "CitationValidationFailed";
        }

        if (InsufficientEvidencePatterns.Any(p => answer.Contains(p, StringComparison.OrdinalIgnoreCase)))
        {
            return "InsufficientEvidence";
        }

        return "Answered";
    }

    private async Task<ResearchAskResponse> PersistTraceAndReturnAsync(
        ResearchAskRequest request,
        ResearchAskResponse response,
        CancellationToken cancellationToken,
        Activity? activity = null,
        IReadOnlyList<StepInput>? steps = null)
    {
        try
        {
            var runId = await _traceService.PersistAskAsync(
                _currentUser.UserId, request, response, steps, cancellationToken);
            activity?.SetTag("research.run_id", runId.ToString());
            return response with { ResearchRunId = runId };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist research ask trace for ticker {Ticker}", request.Ticker);
            activity?.SetTag("trace.persistence_failed", true);
            return response;
        }
    }

    private IReadOnlyList<RetrievedDocumentChunk> ApplyCandidateScoreGate(
        IReadOnlyList<RetrievedDocumentChunk> chunks,
        List<RankingDecision> decisions)
    {
        if (chunks.Count == 0)
        {
            return chunks;
        }

        var eligible = new List<RetrievedDocumentChunk>();
        foreach (var chunk in chunks)
        {
            if (chunk.Result.RelevanceScore < _options.MinimumCandidateScore)
            {
                decisions.Add(new RankingDecision(
                    chunk,
                    null,
                    null,
                    null,
                    null,
                    "DiscardedByMinimumCandidateScore",
                    $"Chunk relevance score was below MinimumCandidateScore {_options.MinimumCandidateScore}.",
                    null));
                continue;
            }

            eligible.Add(chunk);
        }

        if (decisions.Count > 0)
        {
            _logger.LogInformation(
                "{BelowThresholdCount} of {CandidateCount} candidates discarded by MinimumCandidateScore {MinimumCandidateScore}",
                decisions.Count,
                chunks.Count,
                _options.MinimumCandidateScore);
        }

        return eligible;
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

    private IReadOnlyList<RetrievedDocumentChunk> MergeLocalAndWeb(
        IReadOnlyList<RetrievedDocumentChunk> local,
        IReadOnlyList<RetrievedDocumentChunk> web,
        int topK)
    {
        var merged = new List<RetrievedDocumentChunk>();
        var seenChunkIds = new HashSet<Guid>();
        var seenContent = new HashSet<string>(StringComparer.Ordinal);

        void AddIfUnique(RetrievedDocumentChunk chunk)
        {
            if (merged.Count >= topK)
            {
                return;
            }

            if (!seenChunkIds.Add(chunk.Result.DocumentChunkId))
            {
                return;
            }

            if (!seenContent.Add(chunk.Result.Content))
            {
                return;
            }

            merged.Add(chunk);
        }

        foreach (var chunk in local)
        {
            AddIfUnique(chunk);
        }

        var webAdded = 0;
        foreach (var chunk in web)
        {
            if (webAdded >= _options.WebContextLimit)
            {
                break;
            }

            var before = merged.Count;
            AddIfUnique(chunk);
            if (merged.Count > before)
            {
                webAdded++;
            }
        }

        return merged;
    }

    private async Task<ResearchAskResponse> BuildInsufficientEvidenceResponseAsync(
        ResearchAskRequest request,
        IntentDetectionResult intentDetection,
        ResearchRetrievalStrategy strategy,
        RankedSelection rankedSelection,
        long searchMs,
        long rerankMs,
        Stopwatch totalStopwatch,
        Activity? askActivity,
        IReadOnlyList<StepInput>? steps = null)
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
                null,
                finalCitationCount: 0)
            : null;

        var response = new ResearchAskResponse(
            Question: request.Question,
            Answer: InsufficientEvidenceAnswer,
            Model: string.Empty,
            RetrievalStrategy: strategy,
            Citations: [],
            Trace: trace,
            Status: "InsufficientEvidence");

        return await PersistTraceAndReturnAsync(request, response, CancellationToken.None, askActivity, steps);
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
        string? retrievalNote,
        int finalCitationCount = 0)
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
            Retrieval: new ResearchTraceRetrievalSummary(candidateCount, selectedCount, candidateCount - selectedCount, finalCitationCount, discardedByReason),
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
