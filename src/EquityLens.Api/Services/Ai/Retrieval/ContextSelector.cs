using EquityLens.Api.Contracts.Research;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Services.Ai.Retrieval;

public sealed class ContextSelector : IContextSelector
{
    private const string AnnualReportDocumentType = "AnnualReport";
    private const string EarningsPresentationDocumentType = "EarningsPresentation";
    private const string PrimarySourceRole = "Primary";
    private readonly RetrievalOptions _options;
    private readonly ILogger<ContextSelector> _logger;

    public ContextSelector(IOptions<RetrievalOptions> options, ILogger<ContextSelector> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public ContextSelection Select(
        RankedSelection ranked,
        ResearchQuestionIntent intent,
        ResearchRetrievalStrategy strategy)
    {
        var belowFinalThreshold = ranked.Decisions.Count(d =>
            d.Decision == "DiscardedByMinimumFinalScore");
        if (belowFinalThreshold > 0)
        {
            _logger.LogInformation(
                "{BelowThresholdCount} candidates discarded by MinimumFinalScore {MinimumFinalScore}",
                belowFinalThreshold,
                _options.MinimumFinalScore);
        }

        var selected = ranked.SelectedResults
            .Select((chunk, index) => new SelectedChunk(chunk, index + 1))
            .ToList();

        var retrievalNote = BuildRetrievalNote(intent, strategy, ranked.SelectedResults);

        return new ContextSelection(selected, retrievalNote);
    }

    private static string? BuildRetrievalNote(
        ResearchQuestionIntent intent,
        ResearchRetrievalStrategy strategy,
        IReadOnlyList<RetrievedDocumentChunk> selectedResults)
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
}
