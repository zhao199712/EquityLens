using System.ComponentModel.DataAnnotations;

namespace EquityLens.Api.Services.Ai;

public sealed class RetrievalOptions
{
    public const string SectionName = "Retrieval";

    [Range(1, 100)]
    public int DefaultTopK { get; set; } = 10;

    [Range(1, 100)]
    public int MaxTopK { get; set; } = 20;

    [Range(-1.0, 2.0)]
    public double MinimumCandidateScore { get; set; } = 0.0;

    [Range(-1.0, 2.0)]
    public double MinimumFinalScore { get; set; } = 0.0;

    [Range(-1.0, 1.0)]
    public double PrimarySourceBonus { get; set; } = 0.05;

    [Range(-1.0, 1.0)]
    public double RiskEvidenceBonus { get; set; } = 0.10;

    [Range(-1.0, 1.0)]
    public double FinancialEvidenceBonus { get; set; } = 0.12;

    [Range(-1.0, 1.0)]
    public double OutlookEvidenceBonus { get; set; } = 0.12;

    [Range(0.0, 1.0)]
    public double AgendaPenalty { get; set; } = 0.20;

    [Range(0.0, 1.0)]
    public double SafeHarborPenalty { get; set; } = 0.15;

    [Range(0.0, 1.0)]
    public double FirstPagePenalty { get; set; } = 0.10;

    [Range(0.0, 1.0)]
    public double AutoPrimaryRatio { get; set; } = 0.7;


    [Range(0, 10)]
    public int MaxSafeHarborChunks { get; set; } = 1;

    [Range(0, 5)]
    public int MaxCitationRetries { get; set; } = 1;

    [Range(1, 200)]
    public int LocalCandidateCountForRerank { get; set; } = 30;

    [Range(1, 50)]
    public int WebSearchCandidateCount { get; set; } = 10;

    [Range(0, 20)]
    public int WebContextLimit { get; set; } = 3;

    [Range(500, 10000)]
    public int WebSearchMaxContentLength { get; set; } = 3000;

    public string WebSearchFreshness { get; set; } = "month";

    public bool EnableJinaRerank { get; set; } = false;

    public string RetrievalMode { get; set; } = "Hybrid";
}
