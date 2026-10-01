namespace EquityLens.Api.Services.RiskAnalysis;

internal static class RiskModelDefaults
{
    internal const string DailyInterval = "1d";
    // 最低共同交易日門檻(約一個月),讓 1M 以上區間可估算風險;所有持倉必須在相同日期具有有效價格。
    internal const int MinPriceCount = 20;
    internal const decimal MaxConfidenceLevel = 0.999m;
    internal const decimal MinConfidenceLevel = 0.90m;
    internal const int MaxSimulations = 100000;
    internal const int MinSimulations = 1000;
    internal const decimal EwmaLambda = 0.94m;
    internal const string MvewmaFhsModel = "mvewma_fhs";
    internal const string ConservativeMvewmaFhsModel = "mvewma_fhs_conservative";
    internal const decimal ConservativeResidualCapQuantile = 0.99m;
    internal const string VolatilityMethod = "EWMA";
    internal const string DriftAssumption = "ZeroDrift";
    internal static readonly int[] SupportedHorizons = [1, 7, 30];
}
