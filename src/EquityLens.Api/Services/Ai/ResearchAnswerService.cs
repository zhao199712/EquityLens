using System.Diagnostics;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Observability;
using EquityLens.Api.Services.Documents;

namespace EquityLens.Api.Services.Ai;

enum ResearchQuestionIntent
{
    Risk,
    Financial,
    Outlook,
    General
}

public sealed class ResearchAnswerService : IResearchAnswerService
{
    private const string AnnualReportDocumentType = "AnnualReport";
    private const string EarningsPresentationDocumentType = "EarningsPresentation";
    private const string PrimarySourceRole = "Primary";
    private const string SupportingSourceRole = "Supporting";
    private const int DefaultTopK = 10;
    private const int MaxTopK = 20;
    private readonly IDocumentSearchService _documentSearchService;
    private readonly IChatCompletionService _chatCompletion;
    private readonly ILogger<ResearchAnswerService> _logger;

    public ResearchAnswerService(
        IDocumentSearchService documentSearchService,
        IChatCompletionService chatCompletion,
        ILogger<ResearchAnswerService> logger)
    {
        _documentSearchService = documentSearchService;
        _chatCompletion = chatCompletion;
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
        var topK = Math.Clamp(request.TopK <= 0 ? DefaultTopK : request.TopK, 1, MaxTopK);
        IntentDetectionResult intentDetection;
        using (var intentActivity = EquityLensTelemetry.ActivitySource.StartActivity("intent.detect"))
        {
            intentDetection = DetectQuestionIntent(request.Question);
            intentActivity?.SetTag("intent", intentDetection.Selected.ToString());
            intentActivity?.SetStatus(ActivityStatusCode.Ok);
        }
        var intent = intentDetection.Selected;
        ResearchRetrievalStrategy strategy;
        using (var planActivity = EquityLensTelemetry.ActivitySource.StartActivity("retrieval.plan"))
        {
            strategy = BuildRetrievalStrategy(request, topK, intent);
            planActivity?.SetTag("retrieval.mode", strategy.Mode);
            planActivity?.SetTag("retrieval.search_count", strategy.Searches.Count);
            planActivity?.SetStatus(ActivityStatusCode.Ok);
        }
        var results = new List<PlannedSearchResult>();
        var duplicateResults = new List<RankingDecision>();
        var seenChunkIds = new HashSet<Guid>();

        for (var searchIndex = 0; searchIndex < strategy.Searches.Count; searchIndex++)
        {
            var search = strategy.Searches[searchIndex];
            var searchId = $"search-{searchIndex + 1}";
            using var searchActivity = EquityLensTelemetry.ActivitySource.StartActivity("retrieval.search");
            searchActivity?.SetTag("search.id", searchId);
            searchActivity?.SetTag("source.role", search.SourceRole);
            searchActivity?.SetTag("document.type", search.DocumentType);
            searchActivity?.SetTag("retrieval.top_k", search.TopK);

            DocumentSearchResponse searchResponse;
            try
            {
                searchResponse = await _documentSearchService.SearchAsync(
                    new DocumentSearchRequest(
                        Query: search.Query,
                        Ticker: request.Ticker,
                        DocumentType: search.DocumentType,
                        TopK: search.TopK),
                    cancellationToken);
                searchActivity?.SetTag("candidate.count", searchResponse.Results.Count);
                searchActivity?.SetStatus(ActivityStatusCode.Ok);
            }
            catch (Exception exception)
            {
                EquityLensTelemetry.MarkError(searchActivity, exception);
                throw;
            }

            foreach (var result in searchResponse.Results.OrderByDescending(r => r.RelevanceScore))
            {
                if (seenChunkIds.Add(result.DocumentChunkId))
                {
                    results.Add(new PlannedSearchResult(result, search.SourceRole, searchId, search.Query));
                }
                else
                {
                    duplicateResults.Add(new RankingDecision(
                        new PlannedSearchResult(result, search.SourceRole, searchId, search.Query),
                        null,
                        null,
                        null,
                        null,
                        "DiscardedDuplicateChunk",
                        "Chunk was returned by more than one retrieval search.",
                        null));
                }
            }
        }

        searchStopwatch.Stop();
        var rerankStopwatch = Stopwatch.StartNew();
        var rankedSelection = RankPlannedResults(intent, results, topK);
        results = rankedSelection.SelectedResults;
        var retrievalNote = BuildRetrievalNote(intent, strategy, results);
        rerankStopwatch.Stop();

        var context = new List<string>();
        for (var i = 0; i < results.Count; i++)
        {
            var r = results[i].Result;
            var pageInfo = r.PageNumber.HasValue ? $" (Page {r.PageNumber})" : "";
            var documentType = string.IsNullOrWhiteSpace(r.DocumentType) ? "Unknown" : r.DocumentType;
            context.Add($"[{i + 1}] {r.DocumentTitle}{pageInfo} | DocumentType: {documentType} | SourceRole: {results[i].SourceRole}\n{r.Content}");
        }

        var contextText = string.Join("\n\n---\n\n", context);
        var systemPrompt = BuildSystemPrompt(retrievalNote);
        var userPrompt = BuildUserPrompt(request.Question, contextText, retrievalNote);

        var generationStopwatch = Stopwatch.StartNew();
        ChatCompletionResult chatResult;
        using (var llmActivity = EquityLensTelemetry.ActivitySource.StartActivity("llm.complete"))
        {
            llmActivity?.SetTag("llm.provider", _chatCompletion.Provider);
            llmActivity?.SetTag("llm.model", _chatCompletion.Model);
            try
            {
                chatResult = await _chatCompletion.CompleteAsync(
                    new ChatCompletionRequest(SystemPrompt: systemPrompt, UserPrompt: userPrompt, Temperature: request.Temperature),
                    cancellationToken);
                llmActivity?.SetTag("llm.prompt_tokens", chatResult.PromptTokens);
                llmActivity?.SetTag("llm.completion_tokens", chatResult.CompletionTokens);
                llmActivity?.SetStatus(ActivityStatusCode.Ok);

                var promptTags = new TagList
                {
                    { "direction", "prompt" },
                    { "provider", _chatCompletion.Provider },
                    { "model", chatResult.Model }
                };
                var completionTags = new TagList
                {
                    { "direction", "completion" },
                    { "provider", _chatCompletion.Provider },
                    { "model", chatResult.Model }
                };
                EquityLensTelemetry.LlmTokens.Add(chatResult.PromptTokens, promptTags);
                EquityLensTelemetry.LlmTokens.Add(chatResult.CompletionTokens, completionTags);
            }
            catch (Exception exception)
            {
                EquityLensTelemetry.MarkError(llmActivity, exception);
                throw;
            }
        }
        generationStopwatch.Stop();

        var citations = results.Select((r, i) => new ResearchCitation(
            Index: i + 1,
            DocumentChunkId: r.Result.DocumentChunkId,
            DocumentId: r.Result.DocumentId,
            DocumentTitle: r.Result.DocumentTitle,
            DocumentType: r.Result.DocumentType,
            SourceRole: r.SourceRole,
            PageNumber: r.Result.PageNumber,
            QuoteText: r.Result.Content.Length <= 300 ? r.Result.Content : r.Result.Content[..300] + "...",
            RelevanceScore: r.Result.RelevanceScore
        )).ToList();

        totalStopwatch.Stop();
        var trace = request.Debug
            ? BuildTrace(
                intentDetection,
                strategy,
                [.. rankedSelection.Decisions, .. duplicateResults],
                chatResult,
                searchStopwatch.ElapsedMilliseconds,
                rerankStopwatch.ElapsedMilliseconds,
                generationStopwatch.ElapsedMilliseconds,
                totalStopwatch.ElapsedMilliseconds,
                retrievalNote)
            : null;

        var allDecisions = rankedSelection.Decisions.Count + duplicateResults.Count;
        var selectedCount = rankedSelection.Decisions.Count(decision => decision.Decision == "Selected");
        askActivity?.SetTag("intent", intent.ToString());
        askActivity?.SetTag("retrieval.mode", strategy.Mode);
        askActivity?.SetTag("candidate.count", allDecisions);
        askActivity?.SetTag("selected.count", selectedCount);
        askActivity?.SetTag("discarded.count", allDecisions - selectedCount);
        askActivity?.SetTag("llm.provider", _chatCompletion.Provider);
        askActivity?.SetTag("llm.model", chatResult.Model);
        askActivity?.SetStatus(ActivityStatusCode.Ok);
        outcome = "success";

        _logger.LogInformation(
            "Research ask completed with {CandidateCount} candidates, {SelectedCount} selected, provider {Provider}, model {Model}, duration {DurationMs} ms",
            allDecisions,
            selectedCount,
            _chatCompletion.Provider,
            chatResult.Model,
            totalStopwatch.ElapsedMilliseconds);

        return new ResearchAskResponse(
            Question: request.Question,
            Answer: chatResult.Content,
            Model: chatResult.Model,
            RetrievalStrategy: strategy,
            Citations: citations,
            Trace: trace);
        }
        catch (Exception exception)
        {
            EquityLensTelemetry.MarkError(askActivity, exception);
            _logger.LogError(
                "Research ask failed with {ErrorType}, provider {Provider}, model {Model}",
                exception.GetType().Name,
                _chatCompletion.Provider,
                _chatCompletion.Model);
            throw;
        }
        finally
        {
            totalStopwatch.Stop();
            var tags = new TagList
            {
                { "outcome", outcome },
                { "provider", _chatCompletion.Provider },
                { "model", _chatCompletion.Model }
            };
            EquityLensTelemetry.AskRequests.Add(1, tags);
            EquityLensTelemetry.AskDuration.Record(totalStopwatch.Elapsed.TotalMilliseconds, tags);
        }
    }

    private static IntentDetectionResult DetectQuestionIntent(string question)
    {
        var normalized = question.ToLowerInvariant();
        var riskKeywords = new[] { "風險", "risk", "challenge", "uncertainty", "headwind", "不確定", "挑戰" };
        var financialKeywords = new[] { "營收", "revenue", "毛利", "gross margin", "operating margin", "eps", "每股盈餘", "現金流", "cash flow", "income statement", "balance sheet", "損益", "資產負債", "財務" };
        var outlookKeywords = new[] { "展望", "outlook", "guidance", "forecast", "management expects", "management expect", "future outlook", "business outlook", "管理層預期", "管理層展望" };

        var matchedRiskKeywords = FindMatches(normalized, riskKeywords);
        if (matchedRiskKeywords.Count > 0)
        {
            return new IntentDetectionResult(ResearchQuestionIntent.Risk, matchedRiskKeywords, 1.0);
        }

        var matchedFinancialKeywords = FindMatches(normalized, financialKeywords);
        if (matchedFinancialKeywords.Count > 0)
        {
            return new IntentDetectionResult(ResearchQuestionIntent.Financial, matchedFinancialKeywords, 1.0);
        }

        var matchedOutlookKeywords = FindMatches(normalized, outlookKeywords);
        if (matchedOutlookKeywords.Count > 0)
        {
            return new IntentDetectionResult(ResearchQuestionIntent.Outlook, matchedOutlookKeywords, 1.0);
        }

        return new IntentDetectionResult(ResearchQuestionIntent.General, [], 0.5);
    }

    private static ResearchRetrievalStrategy BuildRetrievalStrategy(ResearchAskRequest request, int topK, ResearchQuestionIntent intent)
    {
        if (IsDocumentTypeOverride(request.DocumentType))
        {
            var documentType = request.DocumentType!.Trim();
            ValidateDocumentType(documentType);

            return new ResearchRetrievalStrategy(
                "DocumentTypeOverride",
                [new ResearchRetrievalSearch(documentType, PrimarySourceRole, request.Question, topK, "request specified documentType")]);
        }

        var mode = string.IsNullOrWhiteSpace(request.RetrievalMode)
            ? "Auto"
            : request.RetrievalMode.Trim();

        return mode.ToUpperInvariant() switch
        {
            "CONFERENCEONLY" => new ResearchRetrievalStrategy(
                "ConferenceOnly",
                [new ResearchRetrievalSearch(EarningsPresentationDocumentType, PrimarySourceRole, ExpandConferenceQuery(request.Question, intent), topK, "request selected conference-only retrieval")]),
            "ANNUALREPORTONLY" => new ResearchRetrievalStrategy(
                "AnnualReportOnly",
                [new ResearchRetrievalSearch(AnnualReportDocumentType, PrimarySourceRole, ExpandAnnualReportQuery(request.Question, intent), topK, "request selected annual-report-only retrieval")]),
            "ALLDOCUMENTS" => new ResearchRetrievalStrategy(
                "AllDocuments",
                [new ResearchRetrievalSearch(null, PrimarySourceRole, request.Question, topK, "request selected all-document retrieval")]),
            _ => BuildAutoRetrievalStrategy(request.Question, topK, intent)
        };
    }

    private static ResearchRetrievalStrategy BuildAutoRetrievalStrategy(string question, int topK, ResearchQuestionIntent intent)
    {
        var normalized = question.ToLowerInvariant();
        var mentionsConference = ContainsAny(normalized, ["法說", "conference", "earnings", "presentation", "management", "guidance", "展望"]);
        var mentionsAnnualReport = ContainsAny(normalized, ["年報", "annual report", "財報", "financial statement", "現金流", "資產負債", "損益"]);

        if (mentionsConference || (intent is ResearchQuestionIntent.Risk && !mentionsAnnualReport))
        {
            var (conferenceK, annualK) = SplitTopK(topK, 0.7);
            return new ResearchRetrievalStrategy(
                "Auto",
                BuildSearches(
                    new ResearchRetrievalSearch(EarningsPresentationDocumentType, PrimarySourceRole, ExpandConferenceQuery(question, intent), conferenceK, "primary: conference / management discussion"),
                    new ResearchRetrievalSearch(AnnualReportDocumentType, SupportingSourceRole, ExpandAnnualReportQuery(question, intent), annualK, "supporting: annual report disclosure")));
        }

        if (mentionsAnnualReport)
        {
            var (annualK, conferenceK) = SplitTopK(topK, 0.7);
            return new ResearchRetrievalStrategy(
                "Auto",
                BuildSearches(
                    new ResearchRetrievalSearch(AnnualReportDocumentType, PrimarySourceRole, ExpandAnnualReportQuery(question, intent), annualK, "primary: annual report or financial statements"),
                    new ResearchRetrievalSearch(EarningsPresentationDocumentType, SupportingSourceRole, ExpandConferenceQuery(question, intent), conferenceK, "supporting: management context")));
        }

        return new ResearchRetrievalStrategy(
            "Auto",
            [new ResearchRetrievalSearch(null, PrimarySourceRole, question, topK, "no specific source intent detected")]);
    }

    private static (int Primary, int Secondary) SplitTopK(int topK, double primaryRatio)
    {
        if (topK == 1)
        {
            return (1, 0);
        }

        var primary = Math.Clamp((int)Math.Ceiling(topK * primaryRatio), 1, topK - 1);
        return (primary, topK - primary);
    }

    private static IReadOnlyList<ResearchRetrievalSearch> BuildSearches(params ResearchRetrievalSearch[] searches)
    {
        return searches.Where(search => search.TopK > 0).ToList();
    }

    private static RankedSelectionResult RankPlannedResults(ResearchQuestionIntent intent, IReadOnlyList<PlannedSearchResult> results, int topK)
    {
        var safeHarborKept = false;
        var selectedCandidates = new List<PlannedSearchResult>();
        var decisions = new List<RankingDecision>();
        var seenPages = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var seenContentKeys = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        List<RankedCandidate> ranked;
        using (var rerankActivity = EquityLensTelemetry.ActivitySource.StartActivity("rerank"))
        {
            rerankActivity?.SetTag("candidate.count", results.Count);
            var rankBeforeRerank = results
                .OrderByDescending(result => result.Result.RelevanceScore)
                .Select((result, index) => new { result.Result.DocumentChunkId, Rank = index + 1 })
                .ToDictionary(x => x.DocumentChunkId, x => x.Rank);

            ranked = results
                .Select(result => new
                {
                    Result = result,
                    ScoreBreakdown = ScorePlannedResult(result, intent),
                    RankBeforeRerank = rankBeforeRerank[result.Result.DocumentChunkId]
                })
                .OrderByDescending(x => x.ScoreBreakdown.Final)
                .ThenByDescending(x => x.Result.Result.RelevanceScore)
                .Select((x, index) => new RankedCandidate(
                    x.Result,
                    x.ScoreBreakdown,
                    x.RankBeforeRerank,
                    index + 1))
                .ToList();
            rerankActivity?.SetStatus(ActivityStatusCode.Ok);
        }

        using var deduplicateActivity = EquityLensTelemetry.ActivitySource.StartActivity("deduplicate");
        foreach (var item in ranked)
        {
            if (!ShouldKeepForIntent(intent, item.Result))
            {
                decisions.Add(new RankingDecision(
                    item.Result,
                    item.ScoreBreakdown.Final,
                    item.ScoreBreakdown,
                    item.RankBeforeRerank,
                    item.RankAfterRerank,
                    "DiscardedByRiskEvidenceFilter",
                    "Risk intent but chunk does not contain explicit risk evidence.",
                    null));
                continue;
            }

            var result = item.Result.Result;
            var isSafeHarbor = IsSafeHarbor(result.Content);
            if (isSafeHarbor && safeHarborKept)
            {
                decisions.Add(new RankingDecision(
                    item.Result,
                    item.ScoreBreakdown.Final,
                    item.ScoreBreakdown,
                    item.RankBeforeRerank,
                    item.RankAfterRerank,
                    "DiscardedBySafeHarborLimit",
                    "Only one Safe Harbor chunk is allowed in selected context.",
                    null));
                continue;
            }

            if (result.PageNumber.HasValue)
            {
                var pageKey = $"{result.DocumentId}:{result.PageNumber.Value}";
                if (seenPages.TryGetValue(pageKey, out var duplicatePageChunkId))
                {
                    decisions.Add(new RankingDecision(
                        item.Result,
                        item.ScoreBreakdown.Final,
                        item.ScoreBreakdown,
                        item.RankBeforeRerank,
                        item.RankAfterRerank,
                        "DiscardedByPageDedup",
                        "Chunk was removed because another selected chunk from the same document page was kept.",
                        duplicatePageChunkId));
                    continue;
                }
            }

            var contentKey = BuildContentDedupKey(result.Content);
            if (!string.IsNullOrEmpty(contentKey) && seenContentKeys.TryGetValue(contentKey, out var duplicateContentChunkId))
            {
                decisions.Add(new RankingDecision(
                    item.Result,
                    item.ScoreBreakdown.Final,
                    item.ScoreBreakdown,
                    item.RankBeforeRerank,
                    item.RankAfterRerank,
                    "DiscardedByContentDedup",
                    "Chunk was removed because another selected chunk had near-duplicate normalized content.",
                    duplicateContentChunkId));
                continue;
            }

            if (selectedCandidates.Count == topK)
            {
                decisions.Add(new RankingDecision(
                    item.Result,
                    item.ScoreBreakdown.Final,
                    item.ScoreBreakdown,
                    item.RankBeforeRerank,
                    item.RankAfterRerank,
                    "DiscardedByTopK",
                    "Chunk ranked below the selected topK candidates.",
                    null));
                continue;
            }

            selectedCandidates.Add(item.Result);
            if (result.PageNumber.HasValue)
            {
                seenPages[$"{result.DocumentId}:{result.PageNumber.Value}"] = result.DocumentChunkId;
            }
            if (!string.IsNullOrEmpty(contentKey))
            {
                seenContentKeys[contentKey] = result.DocumentChunkId;
            }
            if (isSafeHarbor)
            {
                safeHarborKept = true;
            }
            decisions.Add(new RankingDecision(
                item.Result,
                item.ScoreBreakdown.Final,
                item.ScoreBreakdown,
                item.RankBeforeRerank,
                item.RankAfterRerank,
                "Selected",
                "Chunk passed intent filters and deduplication, then ranked within topK.",
                null));
        }

        selectedCandidates = selectedCandidates
            .OrderBy(result => result.SourceRole == "Primary" ? 0 : 1)
            .ToList();

        deduplicateActivity?.SetTag("candidate.count", ranked.Count);
        deduplicateActivity?.SetTag("selected.count", selectedCandidates.Count);
        deduplicateActivity?.SetTag("discarded.count", decisions.Count - selectedCandidates.Count);
        deduplicateActivity?.SetStatus(ActivityStatusCode.Ok);

        return new RankedSelectionResult(selectedCandidates, decisions);
    }

    private static string BuildContentDedupKey(string content)
    {
        const int maxLength = 120;
        var normalized = string.Join(" ", content
            .ToLowerInvariant()
            .Replace("\n", " ")
            .Replace("\t", " ")
            .Replace(",", " ")
            .Replace(".", " ")
            .Replace("|", " ")
            .Replace("/", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim())
            .Where(w => w.Length > 0))
            .Trim();

        if (normalized.Length > maxLength)
        {
            normalized = normalized[..maxLength];
        }

        return normalized;
    }

    private static ResearchTraceScoreBreakdown ScorePlannedResult(PlannedSearchResult plannedResult, ResearchQuestionIntent intent)
    {
        var result = plannedResult.Result;
        var content = result.Content;
        var embedding = result.RelevanceScore;
        var primaryBonus = plannedResult.SourceRole == PrimarySourceRole ? 0.05 : 0;
        var riskEvidenceBonus = 0d;
        var financialEvidenceBonus = 0d;
        var outlookEvidenceBonus = 0d;
        var agendaPenalty = 0d;
        var safeHarborPenalty = 0d;

        switch (intent)
        {
            case ResearchQuestionIntent.Risk:
                riskEvidenceBonus = HasRiskEvidence(content) ? 0.10 : 0;
                outlookEvidenceBonus = ContainsAny(content, ["outlook", "guidance", "demand", "margin", "inventory", "headwind", "展望", "業績展望", "需求", "毛利", "庫存"]) ? 0.08 : 0;
                agendaPenalty = ContainsAny(content, ["Agenda", "會議議程", "綜合損益表", "資產負債表"]) ? -0.20 : 0;
                safeHarborPenalty = IsSafeHarbor(content) ? -0.15 : 0;
                break;
            case ResearchQuestionIntent.Financial:
                financialEvidenceBonus = ContainsAny(content, ["revenue", "gross margin", "operating margin", "eps", "income statement", "balance sheet", "cash flow", "營收", "毛利", "營業利益率", "每股盈餘", "現金流", "綜合損益表", "資產負債表"]) ? 0.12 : 0;
                outlookEvidenceBonus = ContainsAny(content, ["outlook", "guidance", "demand", "margin", "展望", "業績展望", "管理層"]) ? 0.08 : 0;
                agendaPenalty = ContainsAny(content, ["Agenda", "會議議程"]) ? -0.20 : 0;
                safeHarborPenalty = IsSafeHarbor(content) ? -0.15 : 0;
                break;
            case ResearchQuestionIntent.Outlook:
                outlookEvidenceBonus = ContainsAny(content, ["outlook", "guidance", "future outlook", "business outlook", "management expects", "management expect", "demand", "key messages", "展望", "業績展望", "管理層預期", "管理層展望", "需求", "重點訊息"]) ? 0.12 : 0;
                financialEvidenceBonus = ContainsAny(content, ["revenue", "margin", "營收", "毛利"]) ? 0.06 : 0;
                agendaPenalty = ContainsAny(content, ["Agenda", "會議議程"]) ? -0.20 : 0;
                safeHarborPenalty = IsSafeHarbor(content) ? -0.15 : 0;
                break;
            default:
                agendaPenalty = ContainsAny(content, ["Agenda", "會議議程"]) ? -0.10 : 0;
                safeHarborPenalty = IsSafeHarbor(content) ? -0.10 : 0;
                break;
        }

        var firstPagePenalty = result.PageNumber <= 1 ? -0.10 : 0;
        var final = embedding
            + primaryBonus
            + riskEvidenceBonus
            + financialEvidenceBonus
            + outlookEvidenceBonus
            + agendaPenalty
            + safeHarborPenalty
            + firstPagePenalty;

        return new ResearchTraceScoreBreakdown(
            Embedding: embedding,
            PrimaryBonus: primaryBonus,
            RiskEvidenceBonus: riskEvidenceBonus,
            FinancialEvidenceBonus: financialEvidenceBonus,
            OutlookEvidenceBonus: outlookEvidenceBonus,
            AgendaPenalty: agendaPenalty,
            SafeHarborPenalty: safeHarborPenalty,
            FirstPagePenalty: firstPagePenalty,
            Final: final);
    }

    private static bool ShouldKeepForIntent(ResearchQuestionIntent intent, PlannedSearchResult plannedResult)
    {
        if (intent != ResearchQuestionIntent.Risk)
        {
            return true;
        }

        return HasRiskEvidence(plannedResult.Result.Content);
    }

    private static bool HasRiskEvidence(string content)
    {
        return ContainsAny(content,
            [
                "risk",
                "risks",
                "risk factor",
                "risk factors",
                "uncertainty",
                "uncertainties",
                "challenge",
                "challenges",
                "headwind",
                "headwinds",
                "風險",
                "不確定",
                "挑戰",
                "逆風"
            ]);
    }

    private static bool IsSafeHarbor(string content)
    {
        return ContainsAny(content, ["Safe Harbor Notice", "safe harbor", "forward-looking statements"]);
    }

    private static bool IsDocumentTypeOverride(string? documentType)
    {
        return !string.IsNullOrWhiteSpace(documentType)
            && !string.Equals(documentType.Trim(), "auto", StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateDocumentType(string documentType)
    {
        if (documentType is AnnualReportDocumentType or EarningsPresentationDocumentType)
        {
            return;
        }

        throw new ArgumentException($"Unsupported documentType '{documentType}'. Allowed values: {AnnualReportDocumentType}, {EarningsPresentationDocumentType}.");
    }

    private static string ExpandConferenceQuery(string question, ResearchQuestionIntent intent)
    {
        var baseQuery = question;
        return intent switch
        {
            ResearchQuestionIntent.Financial => $"{baseQuery} 法說會 財務表現 營收 毛利率 營業利益率 每股盈餘 現金流 revenue gross margin operating margin EPS income statement balance sheet cash flow",
            ResearchQuestionIntent.Outlook => $"{baseQuery} 法說會 展望 業績展望 管理層預期 需求 指引 outlook guidance future outlook business outlook management expects demand key messages",
            _ => $"{baseQuery} 法說會 投資人說明會 風險 挑戰 不確定性 展望 業績展望 管理層 demand uncertainty challenge headwind guidance outlook margin pressure geopolitical export control inventory tariff competition customer"
        };
    }

    private static string ExpandAnnualReportQuery(string question, ResearchQuestionIntent intent)
    {
        var baseQuery = question;
        return intent switch
        {
            ResearchQuestionIntent.Financial => $"{baseQuery} 財務報表 營收 毛利率 營業利益率 資產負債 現金流 每股盈餘 財務比率 financial statements balance sheet income statement cash flow EPS",
            ResearchQuestionIntent.Outlook => $"{baseQuery} 公司概況 業務展望 管理層討論 市場風險 MD&A business outlook management discussion risk factors",
            _ => $"{baseQuery} 風險因素 營運風險 市場風險 信用風險 匯率風險 利率風險 流動性風險 地緣政治 出口管制 客戶集中 供應鏈 不確定性 risk factors credit risk market risk liquidity risk foreign exchange risk"
        };
    }

    private static bool ContainsAny(string value, IReadOnlyList<string> candidates)
    {
        return candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> FindMatches(string value, IReadOnlyList<string> candidates)
    {
        return candidates
            .Where(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ResearchTrace BuildTrace(
        IntentDetectionResult intent,
        ResearchRetrievalStrategy strategy,
        IReadOnlyList<RankingDecision> decisions,
        ChatCompletionResult chatResult,
        long searchMs,
        long rerankMs,
        long generationMs,
        long totalMs,
        string? retrievalNote)
    {
        var candidateCount = decisions.Count;
        var selectedCount = decisions.Count(decision => decision.Decision == "Selected");
        var discardedByReason = decisions
            .Where(decision => decision.Decision != "Selected")
            .GroupBy(decision => decision.Decision)
            .ToDictionary(group => group.Key, group => group.Count());

        var traceResults = decisions.Select(decision =>
        {
            var result = decision.Result.Result;
            return new ResearchTraceResult(
                SearchId: decision.Result.SearchId,
                Query: decision.Result.Query,
                DocumentChunkId: result.DocumentChunkId,
                DocumentId: result.DocumentId,
                DocumentTitle: result.DocumentTitle,
                DocumentType: result.DocumentType,
                SourceRole: decision.Result.SourceRole,
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
            Intent: new ResearchTraceIntent(
                Selected: intent.Selected.ToString(),
                MatchedKeywords: intent.MatchedKeywords,
                Confidence: intent.Confidence),
            RetrievalStrategy: strategy,
            Retrieval: new ResearchTraceRetrievalSummary(
                CandidateCount: candidateCount,
                SelectedCount: selectedCount,
                DiscardedCount: candidateCount - selectedCount,
                DiscardedByReason: discardedByReason),
            TokenUsage: new ResearchTraceTokenUsage(
                Model: chatResult.Model,
                PromptTokens: chatResult.PromptTokens,
                CompletionTokens: chatResult.CompletionTokens,
                TotalTokens: chatResult.PromptTokens + chatResult.CompletionTokens),
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

        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength] + "...";
    }

    private static string? BuildRetrievalNote(
        ResearchQuestionIntent intent,
        ResearchRetrievalStrategy strategy,
        IReadOnlyList<PlannedSearchResult> selectedResults)
    {
        if (intent != ResearchQuestionIntent.Risk)
        {
            return null;
        }

        var searchedPrimaryConference = strategy.Searches.Any(search =>
            search.SourceRole == PrimarySourceRole
            && search.DocumentType == EarningsPresentationDocumentType);

        var selectedPrimaryConference = selectedResults.Any(result =>
            result.SourceRole == PrimarySourceRole
            && result.Result.DocumentType == EarningsPresentationDocumentType);

        if (!searchedPrimaryConference || selectedPrimaryConference)
        {
            return null;
        }

        return "系統已搜尋 Primary 來源的法說會簡報，但未找到包含明確風險討論的法說會片段；下方若有 Supporting 來源，僅作為年報風險揭露補充。";
    }

    private static string BuildSystemPrompt(string? retrievalNote)
    {
        var retrievalInstruction = string.IsNullOrWhiteSpace(retrievalNote)
            ? ""
            : $"""

Critical retrieval status:
- {retrievalNote}
- You must explicitly mention this retrieval status in the answer before using Supporting-source disclosures.
- Do not say that conference materials were not searched. Say that conference materials were searched but no explicit risk-discussion excerpt was selected.
""";

        return $"""
You are a professional investment research assistant. Your task is to answer questions based solely on the provided document excerpts.

Rules:
1. Only answer using information from the provided documents.
2. Primary sources are direct evidence for the user's question. Supporting sources provide background or formal disclosure only.
3. Do not attribute Supporting-source facts to Primary sources.
4. If Primary sources lack details but Supporting sources contain relevant information, answer in two sections: "直接來源內容" and "補充來源揭露".
5. If all provided documents do not contain enough information to answer, clearly state "目前提供的資料不足以回答此問題。"
6. Use Traditional Chinese (繁體中文) for all responses.
7. After each important fact or conclusion, cite the source document using the bracket notation [1], [2], etc.
8. Use a neutral, objective, analytical tone.
9. Do not fabricate numbers, dates, or events not present in the provided text.
10. Choose evidence type according to the question intent. Financial statement table pages can support financial metric questions, but financial statement, outlook, or guidance pages should not be used as risk evidence unless they contain explicit risk discussion. Agenda pages and cover pages should not be used as substantive evidence.
11. If multiple documents provide related information, synthesize them while preserving source attribution.
12. For risk questions, never describe Supporting annual-report disclosures as risks mentioned by conference materials.
{retrievalInstruction}
""";
    }

    private static string BuildUserPrompt(string question, string context, string? retrievalNote)
    {
        var noteSection = string.IsNullOrWhiteSpace(retrievalNote)
            ? ""
            : $"""
重要檢索狀態：
{retrievalNote}
回答時必須先說明此狀態；若引用 Supporting 年報資料，請明確標示為年報補充揭露，不可說成法說會直接提到。

""";

        return $"""
以下是與問題相關的文件內容：

{noteSection}
{context}

===

問題：{question}

請根據以上文件回答。如果文件中沒有相關資訊，請明確說明。回答風險題時，請先區分「直接來源內容」與「補充來源揭露」。
""";
    }

    private sealed record IntentDetectionResult(
        ResearchQuestionIntent Selected,
        IReadOnlyList<string> MatchedKeywords,
        double Confidence);

    private sealed record PlannedSearchResult(
        DocumentSearchResult Result,
        string SourceRole,
        string SearchId,
        string Query);

    private sealed record RankedCandidate(
        PlannedSearchResult Result,
        ResearchTraceScoreBreakdown ScoreBreakdown,
        int RankBeforeRerank,
        int RankAfterRerank);

    private sealed record RankedSelectionResult(
        List<PlannedSearchResult> SelectedResults,
        IReadOnlyList<RankingDecision> Decisions);

    private sealed record RankingDecision(
        PlannedSearchResult Result,
        double? AdjustedScore,
        ResearchTraceScoreBreakdown? ScoreBreakdown,
        int? RankBeforeRerank,
        int? RankAfterRerank,
        string Decision,
        string Reason,
        Guid? DuplicateOfChunkId);
}
