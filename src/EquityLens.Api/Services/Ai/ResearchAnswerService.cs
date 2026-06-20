using System.Diagnostics;
using EquityLens.Api.Contracts.Research;
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

    public ResearchAnswerService(
        IDocumentSearchService documentSearchService,
        IChatCompletionService chatCompletion)
    {
        _documentSearchService = documentSearchService;
        _chatCompletion = chatCompletion;
    }

    public async Task<ResearchAskResponse> AskAsync(ResearchAskRequest request, CancellationToken cancellationToken = default)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var retrievalStopwatch = Stopwatch.StartNew();
        var topK = Math.Clamp(request.TopK <= 0 ? DefaultTopK : request.TopK, 1, MaxTopK);
        var intent = DetectQuestionIntent(request.Question);
        var strategy = BuildRetrievalStrategy(request, topK, intent);
        var results = new List<PlannedSearchResult>();
        var duplicateResults = new List<RankingDecision>();
        var seenChunkIds = new HashSet<Guid>();

        foreach (var search in strategy.Searches)
        {
            var searchResponse = await _documentSearchService.SearchAsync(
                new DocumentSearchRequest(
                    Query: search.Query,
                    Ticker: request.Ticker,
                    DocumentType: search.DocumentType,
                    TopK: search.TopK),
                cancellationToken);

            foreach (var result in searchResponse.Results.OrderByDescending(r => r.RelevanceScore))
            {
                if (seenChunkIds.Add(result.DocumentChunkId))
                {
                    results.Add(new PlannedSearchResult(result, search.SourceRole));
                }
                else
                {
                    duplicateResults.Add(new RankingDecision(
                        new PlannedSearchResult(result, search.SourceRole),
                        null,
                        "DiscardedDuplicateChunk",
                        "Chunk was returned by more than one retrieval search."));
                }
            }
        }

        retrievalStopwatch.Stop();
        var rankedSelection = RankPlannedResults(intent, results, topK);
        results = rankedSelection.SelectedResults;
        var retrievalNote = BuildRetrievalNote(intent, strategy, results);

        var context = new List<string>();
        for (var i = 0; i < results.Count; i++)
        {
            var r = results[i].Result;
            var pageInfo = r.PageNumber.HasValue ? $" (Page {r.PageNumber})" : "";
            var documentType = string.IsNullOrWhiteSpace(r.DocumentType) ? "Unknown" : r.DocumentType;
            context.Add($"[{i + 1}] {r.DocumentTitle}{pageInfo} | DocumentType: {documentType} | SourceRole: {results[i].SourceRole}\n{r.Content}");
        }

        var contextText = string.Join("\n\n---\n\n", context);
        var systemPrompt = BuildSystemPrompt();
        var userPrompt = BuildUserPrompt(request.Question, contextText, retrievalNote);

        var generationStopwatch = Stopwatch.StartNew();
        var chatResult = await _chatCompletion.CompleteAsync(
            new ChatCompletionRequest(SystemPrompt: systemPrompt, UserPrompt: userPrompt, Temperature: request.Temperature),
            cancellationToken);
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
                intent,
                strategy,
                [.. rankedSelection.Decisions, .. duplicateResults],
                retrievalStopwatch.ElapsedMilliseconds,
                generationStopwatch.ElapsedMilliseconds,
                totalStopwatch.ElapsedMilliseconds,
                retrievalNote)
            : null;

        return new ResearchAskResponse(
            Question: request.Question,
            Answer: chatResult.Content,
            Model: chatResult.Model,
            RetrievalStrategy: strategy,
            Citations: citations,
            Trace: trace);
    }

    private static ResearchQuestionIntent DetectQuestionIntent(string question)
    {
        var normalized = question.ToLowerInvariant();

        if (ContainsAny(normalized, ["風險", "risk", "challenge", "uncertainty", "headwind", "不確定", "挑戰"]))
        {
            return ResearchQuestionIntent.Risk;
        }

        if (ContainsAny(normalized, ["營收", "revenue", "毛利", "gross margin", "operating margin", "eps", "每股盈餘", "現金流", "cash flow", "income statement", "balance sheet", "損益", "資產負債", "財務"]))
        {
            return ResearchQuestionIntent.Financial;
        }

        if (ContainsAny(normalized, ["展望", "outlook", "guidance", "forecast", "management expects", "management expect", "future outlook", "business outlook", "管理層預期", "管理層展望"]))
        {
            return ResearchQuestionIntent.Outlook;
        }

        return ResearchQuestionIntent.General;
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

        var ranked = results
            .Select(result => new
            {
                Result = result,
                AdjustedScore = ScorePlannedResult(result, intent)
            })
            .OrderByDescending(x => x.AdjustedScore)
            .ThenByDescending(x => x.Result.Result.RelevanceScore);

        foreach (var item in ranked)
        {
            if (!ShouldKeepForIntent(intent, item.Result))
            {
                decisions.Add(new RankingDecision(
                    item.Result,
                    item.AdjustedScore,
                    "DiscardedByRiskEvidenceFilter",
                    "Risk intent but chunk does not contain explicit risk evidence."));
                continue;
            }

            if (IsSafeHarbor(item.Result.Result.Content))
            {
                if (safeHarborKept)
                {
                    decisions.Add(new RankingDecision(
                        item.Result,
                        item.AdjustedScore,
                        "DiscardedBySafeHarborLimit",
                        "Only one Safe Harbor chunk is allowed in selected context."));
                    continue;
                }

                safeHarborKept = true;
            }

            if (selectedCandidates.Count == topK)
            {
                decisions.Add(new RankingDecision(
                    item.Result,
                    item.AdjustedScore,
                    "DiscardedByTopK",
                    "Chunk ranked below the selected topK candidates."));
                continue;
            }

            selectedCandidates.Add(item.Result);
            decisions.Add(new RankingDecision(
                item.Result,
                item.AdjustedScore,
                "SelectedCandidate",
                "Chunk passed intent filters and ranked within topK before final ordering/dedup."));
        }

        selectedCandidates = selectedCandidates
            .OrderBy(result => result.SourceRole == "Primary" ? 0 : 1)
            .ToList();

        var deduped = DeduplicateSelectedResults(selectedCandidates, topK);
        var selectedChunkIds = deduped.Select(result => result.Result.DocumentChunkId).ToHashSet();

        decisions = decisions.Select(decision =>
        {
            if (decision.Decision != "SelectedCandidate")
            {
                return decision;
            }

            return selectedChunkIds.Contains(decision.Result.Result.DocumentChunkId)
                ? decision with
                {
                    Decision = "Selected",
                    Reason = "Chunk selected for final context."
                }
                : decision with
                {
                    Decision = "DiscardedByDedup",
                    Reason = "Chunk was removed by final page/content deduplication."
                };
        }).ToList();

        return new RankedSelectionResult(deduped, decisions);
    }

    private static List<PlannedSearchResult> DeduplicateSelectedResults(IReadOnlyList<PlannedSearchResult> selected, int topK)
    {
        var deduped = new List<PlannedSearchResult>(selected.Count);
        var seenPages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenContentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in selected)
        {
            var result = item.Result;
            var pageKey = $"{result.DocumentId}:{result.PageNumber ?? -1}";

            if (!seenPages.Add(pageKey))
            {
                continue;
            }

            var contentKey = BuildContentDedupKey(result.Content);
            if (!string.IsNullOrEmpty(contentKey) && !seenContentKeys.Add(contentKey))
            {
                continue;
            }

            deduped.Add(item);
            if (deduped.Count == topK)
            {
                break;
            }
        }

        return deduped;
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

    private static double ScorePlannedResult(PlannedSearchResult plannedResult, ResearchQuestionIntent intent)
    {
        var result = plannedResult.Result;
        var score = result.RelevanceScore;

        if (plannedResult.SourceRole == PrimarySourceRole)
        {
            score += 0.05;
        }

        var content = result.Content;

        switch (intent)
        {
            case ResearchQuestionIntent.Risk:
                score = ScoreRiskResult(score, content);
                break;
            case ResearchQuestionIntent.Financial:
                score = ScoreFinancialResult(score, content);
                break;
            case ResearchQuestionIntent.Outlook:
                score = ScoreOutlookResult(score, content);
                break;
            default:
                score = ScoreGeneralResult(score, content);
                break;
        }

        if (result.PageNumber <= 1)
        {
            score -= 0.10;
        }

        return score;
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

    private static double ScoreRiskResult(double score, string content)
    {
        if (HasRiskEvidence(content))
        {
            score += 0.10;
        }
        if (ContainsAny(content, ["outlook", "guidance", "demand", "margin", "inventory", "headwind", "展望", "業績展望", "需求", "毛利", "庫存"]))
        {
            score += 0.08;
        }
        if (ContainsAny(content, ["Agenda", "會議議程", "綜合損益表", "資產負債表"]))
        {
            score -= 0.20;
        }
        if (IsSafeHarbor(content))
        {
            score -= 0.15;
        }
        return score;
    }

    private static double ScoreFinancialResult(double score, string content)
    {
        if (ContainsAny(content, ["revenue", "gross margin", "operating margin", "eps", "income statement", "balance sheet", "cash flow", "營收", "毛利", "營業利益率", "每股盈餘", "現金流", "綜合損益表", "資產負債表"]))
        {
            score += 0.12;
        }
        if (ContainsAny(content, ["outlook", "guidance", "demand", "margin", "展望", "業績展望", "管理層"]))
        {
            score += 0.08;
        }
        if (ContainsAny(content, ["Agenda", "會議議程"]))
        {
            score -= 0.20;
        }
        if (IsSafeHarbor(content))
        {
            score -= 0.15;
        }
        return score;
    }

    private static double ScoreOutlookResult(double score, string content)
    {
        if (ContainsAny(content, ["outlook", "guidance", "future outlook", "business outlook", "management expects", "management expect", "demand", "key messages", "展望", "業績展望", "管理層預期", "管理層展望", "需求", "重點訊息"]))
        {
            score += 0.12;
        }
        if (ContainsAny(content, ["revenue", "margin", "營收", "毛利"]))
        {
            score += 0.06;
        }
        if (ContainsAny(content, ["Agenda", "會議議程"]))
        {
            score -= 0.20;
        }
        if (IsSafeHarbor(content))
        {
            score -= 0.15;
        }
        return score;
    }

    private static double ScoreGeneralResult(double score, string content)
    {
        if (ContainsAny(content, ["Agenda", "會議議程"]))
        {
            score -= 0.10;
        }
        if (IsSafeHarbor(content))
        {
            score -= 0.10;
        }
        return score;
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

    private static ResearchTrace BuildTrace(
        ResearchQuestionIntent intent,
        ResearchRetrievalStrategy strategy,
        IReadOnlyList<RankingDecision> decisions,
        long retrievalMs,
        long generationMs,
        long totalMs,
        string? retrievalNote)
    {
        var traceResults = decisions.Select(decision =>
        {
            var result = decision.Result.Result;
            return new ResearchTraceResult(
                DocumentChunkId: result.DocumentChunkId,
                DocumentId: result.DocumentId,
                DocumentTitle: result.DocumentTitle,
                DocumentType: result.DocumentType,
                SourceRole: decision.Result.SourceRole,
                PageNumber: result.PageNumber,
                RelevanceScore: result.RelevanceScore,
                AdjustedScore: decision.AdjustedScore,
                Selected: decision.Decision == "Selected",
                Decision: decision.Decision,
                Reason: decision.Reason,
                ContentPreview: BuildContentPreview(result.Content));
        }).ToList();

        return new ResearchTrace(
            Intent: intent.ToString(),
            RetrievalStrategy: strategy,
            Results: traceResults,
            LatencyMs: new ResearchTraceLatency(retrievalMs, generationMs, totalMs),
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

    private static string BuildSystemPrompt()
    {
        return """
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
""";
    }

    private static string BuildUserPrompt(string question, string context, string? retrievalNote)
    {
        var noteSection = string.IsNullOrWhiteSpace(retrievalNote)
            ? ""
            : $"""
檢索註記：
{retrievalNote}

""";

        return $"""
以下是與問題相關的文件內容：

{noteSection}
{context}

===

問題：{question}

請根據以上文件回答。如果文件中沒有相關資訊，請明確說明。
""";
    }

    private sealed record PlannedSearchResult(DocumentSearchResult Result, string SourceRole);

    private sealed record RankedSelectionResult(
        List<PlannedSearchResult> SelectedResults,
        IReadOnlyList<RankingDecision> Decisions);

    private sealed record RankingDecision(
        PlannedSearchResult Result,
        double? AdjustedScore,
        string Decision,
        string Reason);
}
