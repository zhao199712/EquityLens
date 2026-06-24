using EquityLens.Api.Contracts.Research;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Services.Ai.Retrieval;

public sealed class RetrievalPlanner : IRetrievalPlanner
{
    private const string AnnualReportDocumentType = "AnnualReport";
    private const string EarningsPresentationDocumentType = "EarningsPresentation";
    private const string PrimarySourceRole = "Primary";
    private const string SupportingSourceRole = "Supporting";
    private readonly RetrievalOptions _options;

    public RetrievalPlanner(IOptions<RetrievalOptions> options)
    {
        _options = options.Value;
    }

    public ResearchRetrievalStrategy BuildPlan(
        string question,
        RetrievalMode? mode,
        string? documentType,
        int topK)
    {
        if (IsDocumentTypeOverride(documentType))
        {
            var trimmedDocumentType = documentType!.Trim();
            ValidateDocumentType(trimmedDocumentType);

            return new ResearchRetrievalStrategy(
                "DocumentTypeOverride",
                [new ResearchRetrievalSearch(trimmedDocumentType, PrimarySourceRole, question, topK, "request specified documentType")]);
        }

        var selectedMode = mode ?? RetrievalMode.Auto;

        return selectedMode switch
        {
            RetrievalMode.ConferenceOnly => new ResearchRetrievalStrategy(
                "ConferenceOnly",
                [new ResearchRetrievalSearch(EarningsPresentationDocumentType, PrimarySourceRole, ExpandConferenceQuery(question, DetectIntent(question)), topK, "request selected conference-only retrieval")]),
            RetrievalMode.AnnualReportOnly => new ResearchRetrievalStrategy(
                "AnnualReportOnly",
                [new ResearchRetrievalSearch(AnnualReportDocumentType, PrimarySourceRole, ExpandAnnualReportQuery(question, DetectIntent(question)), topK, "request selected annual-report-only retrieval")]),
            RetrievalMode.AllDocuments => new ResearchRetrievalStrategy(
                "AllDocuments",
                [new ResearchRetrievalSearch(null, PrimarySourceRole, question, topK, "request selected all-document retrieval")]),
            _ => BuildAutoPlan(question, topK)
        };
    }

    private ResearchRetrievalStrategy BuildAutoPlan(string question, int topK)
    {
        var intent = DetectIntent(question);
        var normalized = question.ToLowerInvariant();
        var mentionsConference = ContainsAny(normalized, ["法說", "conference", "earnings", "presentation", "management", "guidance", "展望"]);
        var mentionsAnnualReport = ContainsAny(normalized, ["年報", "annual report", "財報", "financial statement", "現金流", "資產負債", "損益"]);
        if (mentionsConference || (intent is ResearchQuestionIntent.Risk or ResearchQuestionIntent.Financial && !mentionsAnnualReport))
        {
            var (conferenceK, annualK) = SplitTopK(topK, _options.AutoPrimaryRatio);
            return new ResearchRetrievalStrategy(
                "Auto",
                BuildSearches(
                    new ResearchRetrievalSearch(EarningsPresentationDocumentType, PrimarySourceRole, ExpandConferenceQuery(question, intent), conferenceK, "primary: conference / management discussion"),
                    new ResearchRetrievalSearch(AnnualReportDocumentType, SupportingSourceRole, ExpandAnnualReportQuery(question, intent), annualK, "supporting: annual report disclosure")));
        }

        if (mentionsAnnualReport)
        {
            var (annualK, conferenceK) = SplitTopK(topK, _options.AutoPrimaryRatio);
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

    private static ResearchQuestionIntent DetectIntent(string question)
    {
        var detector = new IntentDetector();
        return detector.Detect(question).Selected;
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
}
