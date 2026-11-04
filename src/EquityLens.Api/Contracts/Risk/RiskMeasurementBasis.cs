namespace EquityLens.Api.Contracts.Risk;

/// <summary>風險數字的期間、組合建構及估計口徑；缺少中繼資料的舊結果不得推測。</summary>
public sealed record RiskMeasurementBasis(
    DateOnly? From, DateOnly? To, string Weighting, string CashTreatment,
    string ReturnMethod, string VolatilityMethod, int? ObservationCount,
    string SampleDefinition, decimal? ConfidenceLevel = null, int? HorizonDays = null);
