using System.Diagnostics;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Observability;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Services.Ai.Retrieval;

public sealed class ResultReranker : IResultReranker
{
    private readonly RetrievalOptions _options;
    private readonly IChunkContentCleaner _contentCleaner;
    private readonly IDocumentReranker? _reranker;

    public ResultReranker(
        IOptions<RetrievalOptions> options,
        IChunkContentCleaner contentCleaner,
        IDocumentReranker? reranker = null)
    {
        _options = options.Value;
        _contentCleaner = contentCleaner;
        _reranker = reranker;
    }

    public async Task<RankedSelection> Rank(IReadOnlyList<RetrievedDocumentChunk> chunks, ResearchQuestionIntent intent, int topK)
    {
        IReadOnlyList<RetrievedDocumentChunk> rerankedChunks = chunks;

        if (_options.RerankProvider != "None" && _reranker is not null)
        {
            var query = chunks.FirstOrDefault()?.Query ?? "";
            rerankedChunks = await _reranker.RerankAsync(query, chunks, Math.Max(topK * 2, _options.LocalCandidateCountForRerank));
        }

        var safeHarborKeptCount = 0;
        var selectedCandidates = new List<RetrievedDocumentChunk>();
        var decisions = new List<RankingDecision>();
        var seenPages = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var seenContentKeys = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        List<RankedChunk> ranked;

        using (var rerankActivity = EquityLensTelemetry.ActivitySource.StartActivity("rerank"))
        {
            rerankActivity?.SetTag("candidate.count", rerankedChunks.Count);
            var rankBeforeRerank = rerankedChunks
                .OrderByDescending(chunk => chunk.Result.RelevanceScore)
                .Select((chunk, index) => new { chunk.Result.DocumentChunkId, Rank = index + 1 })
                .ToDictionary(x => x.DocumentChunkId, x => x.Rank);

            ranked = rerankedChunks
                .Select(chunk => new
                {
                    Chunk = chunk,
                    ScoreBreakdown = ScoreChunk(chunk, intent),
                    RankBeforeRerank = rankBeforeRerank[chunk.Result.DocumentChunkId]
                })
                .OrderByDescending(x => x.ScoreBreakdown.Final)
                .ThenByDescending(x => x.Chunk.Result.RelevanceScore)
                .Select((x, index) => new RankedChunk(
                    x.Chunk,
                    x.ScoreBreakdown,
                    x.RankBeforeRerank,
                    index + 1))
                .ToList();
            rerankActivity?.SetStatus(ActivityStatusCode.Ok);
        }

        using var deduplicateActivity = EquityLensTelemetry.ActivitySource.StartActivity("deduplicate");
        foreach (var item in ranked)
        {
            if (!ShouldKeepForIntent(intent, item.Chunk))
            {
                decisions.Add(new RankingDecision(
                    item.Chunk,
                    item.ScoreBreakdown.Final,
                    item.ScoreBreakdown,
                    item.RankBeforeRerank,
                    item.RankAfterRerank,
                    "DiscardedByRiskEvidenceFilter",
                    "Risk intent but chunk does not contain explicit risk evidence.",
                    null));
                continue;
            }

            var result = item.Chunk.Result;
            var isSafeHarbor = IsSafeHarbor(result.Content);
            if (isSafeHarbor && safeHarborKeptCount >= _options.MaxSafeHarborChunks)
            {
                decisions.Add(new RankingDecision(
                    item.Chunk,
                    item.ScoreBreakdown.Final,
                    item.ScoreBreakdown,
                    item.RankBeforeRerank,
                    item.RankAfterRerank,
                    "DiscardedBySafeHarborLimit",
                    $"Only {_options.MaxSafeHarborChunks} Safe Harbor chunk(s) allowed in selected context.",
                    null));
                continue;
            }

            if (result.PageNumber.HasValue)
            {
                var pageKey = $"{result.DocumentId}:{result.PageNumber.Value}";
                if (seenPages.TryGetValue(pageKey, out var duplicatePageChunkId))
                {
                    decisions.Add(new RankingDecision(
                        item.Chunk,
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

            var contentKey = BuildContentDedupKey(_contentCleaner.Clean(result.Content));
            if (!string.IsNullOrEmpty(contentKey) && seenContentKeys.TryGetValue(contentKey, out var duplicateContentChunkId))
            {
                decisions.Add(new RankingDecision(
                    item.Chunk,
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
                    item.Chunk,
                    item.ScoreBreakdown.Final,
                    item.ScoreBreakdown,
                    item.RankBeforeRerank,
                    item.RankAfterRerank,
                    "DiscardedByTopK",
                    "Chunk ranked below the selected topK candidates.",
                    null));
                continue;
            }

            selectedCandidates.Add(item.Chunk);
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
                safeHarborKeptCount++;
            }
            decisions.Add(new RankingDecision(
                item.Chunk,
                item.ScoreBreakdown.Final,
                item.ScoreBreakdown,
                item.RankBeforeRerank,
                item.RankAfterRerank,
                "Selected",
                "Chunk passed intent filters and deduplication, then ranked within topK.",
                null));
        }

        deduplicateActivity?.SetTag("candidate.count", ranked.Count);
        deduplicateActivity?.SetTag("selected.count", selectedCandidates.Count);
        deduplicateActivity?.SetTag("discarded.count", decisions.Count - selectedCandidates.Count);
        deduplicateActivity?.SetStatus(ActivityStatusCode.Ok);

        return new RankedSelection(selectedCandidates, decisions);
    }

    private ResearchTraceScoreBreakdown ScoreChunk(RetrievedDocumentChunk chunk, ResearchQuestionIntent intent)
    {
        var result = chunk.Result;
        var content = result.Content;
        var embedding = result.RelevanceScore;
        var externalRerank = !string.Equals(_options.RerankProvider, "None", StringComparison.OrdinalIgnoreCase);
        var primaryBonus = externalRerank ? 0 : (chunk.SourceRole == "Primary" ? _options.PrimarySourceBonus : 0);
        var riskEvidenceBonus = 0d;
        var financialEvidenceBonus = 0d;
        var outlookEvidenceBonus = 0d;
        var agendaPenalty = 0d;
        var safeHarborPenalty = 0d;

        if (!externalRerank)
        {
            switch (intent)
            {
                case ResearchQuestionIntent.Risk:
                    riskEvidenceBonus = HasRiskEvidence(content) ? _options.RiskEvidenceBonus : 0;
                    outlookEvidenceBonus = ContainsAny(content, ["outlook", "guidance", "demand", "margin", "inventory", "headwind", "展望", "業績展望", "需求", "毛利", "庫存"]) ? _options.OutlookEvidenceBonus * 0.67 : 0;
                    break;
                case ResearchQuestionIntent.Financial:
                    financialEvidenceBonus = ContainsAny(content, ["revenue", "gross margin", "operating margin", "eps", "income statement", "balance sheet", "cash flow", "營收", "毛利", "營業利益率", "每股盈餘", "現金流", "綜合損益表", "資產負債表"]) ? _options.FinancialEvidenceBonus : 0;
                    outlookEvidenceBonus = ContainsAny(content, ["outlook", "guidance", "demand", "margin", "展望", "業績展望", "管理層"]) ? _options.OutlookEvidenceBonus * 0.67 : 0;
                    break;
                case ResearchQuestionIntent.Outlook:
                    outlookEvidenceBonus = ContainsAny(content, ["outlook", "guidance", "future outlook", "business outlook", "management expects", "management expect", "demand", "key messages", "展望", "業績展望", "管理層預期", "管理層展望", "需求", "重點訊息"]) ? _options.OutlookEvidenceBonus : 0;
                    financialEvidenceBonus = ContainsAny(content, ["revenue", "margin", "營收", "毛利"]) ? _options.FinancialEvidenceBonus * 0.5 : 0;
                    break;
            }
        }

        // guardrail penalties always apply regardless of external rerank
        switch (intent)
        {
            case ResearchQuestionIntent.Risk:
                agendaPenalty = ContainsAny(content, ["Agenda", "會議議程", "綜合損益表", "資產負債表"]) ? -_options.AgendaPenalty : 0;
                safeHarborPenalty = IsSafeHarbor(content) ? -_options.SafeHarborPenalty : 0;
                break;
            case ResearchQuestionIntent.Financial:
                agendaPenalty = ContainsAny(content, ["Agenda", "會議議程"]) ? -_options.AgendaPenalty : 0;
                safeHarborPenalty = IsSafeHarbor(content) ? -_options.SafeHarborPenalty : 0;
                break;
            case ResearchQuestionIntent.Outlook:
                agendaPenalty = ContainsAny(content, ["Agenda", "會議議程"]) ? -_options.AgendaPenalty : 0;
                safeHarborPenalty = IsSafeHarbor(content) ? -_options.SafeHarborPenalty : 0;
                break;
            default:
                agendaPenalty = ContainsAny(content, ["Agenda", "會議議程"]) ? -_options.AgendaPenalty * 0.5 : 0;
                safeHarborPenalty = IsSafeHarbor(content) ? -_options.SafeHarborPenalty * 0.67 : 0;
                break;
        }

        var firstPagePenalty = result.PageNumber <= 1 ? -_options.FirstPagePenalty : 0;
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

    private static bool ShouldKeepForIntent(ResearchQuestionIntent intent, RetrievedDocumentChunk chunk)
    {
        if (intent != ResearchQuestionIntent.Risk)
        {
            return true;
        }

        if (HasRiskEvidence(chunk.Result.Content))
        {
            return true;
        }

        return chunk.SourceRole == "Primary"
            && string.Equals(chunk.Result.DocumentType, "EarningsPresentation", StringComparison.OrdinalIgnoreCase)
            && HasOutlookEvidence(chunk.Result.Content);
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

    private static bool HasOutlookEvidence(string content)
    {
        return ContainsAny(content,
            [
                "outlook",
                "guidance",
                "future outlook",
                "business outlook",
                "management expects",
                "management expect",
                "demand",
                "capacity",
                "capex",
                "capital expenditure",
                "cash flow",
                "HPC",
                "AI",
                "展望",
                "業績展望",
                "管理層預期",
                "需求",
                "產能",
                "資本支出",
                "現金流"
            ]);
    }

    private static bool IsSafeHarbor(string content)
    {
        return ContainsAny(content, ["Safe Harbor Notice", "safe harbor", "forward-looking statements"]);
    }

    private static string BuildContentDedupKey(string content)
    {
        const int maxLength = 300;
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

    private static bool ContainsAny(string value, IReadOnlyList<string> candidates)
    {
        return candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
    }
}
