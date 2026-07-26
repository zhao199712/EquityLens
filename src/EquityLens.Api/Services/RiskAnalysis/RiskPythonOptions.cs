namespace EquityLens.Api.Services.RiskAnalysis;

public sealed class RiskPythonOptions
{
    public const string SectionName = "RiskPython";
    public bool PrimaryEnabled { get; set; }
    public bool ShadowEnabled { get; set; }
    public string JobsStreamKey { get; set; } = "equitylens:risk-python:jobs";
    public string JobsConsumerGroup { get; set; } = "risk-python-workers";
    public string ResultsStreamKey { get; set; } = "equitylens:risk-python:results";
    public string ResultsConsumerGroup { get; set; } = "risk-shadow-result-consumers";
    public string DeadLetterStreamKey { get; set; } = "equitylens:risk-python:dead-letter";
    public string PayloadKeyPrefix { get; set; } = "equitylens:risk-python";
    public string CandidateAlgorithmVersion { get; set; } = "vt-garch-t-joint-fhs-v1";
    public string DataFactorVersion { get; set; } = "adjusted-close-db-v1";
    public int PayloadTtlHours { get; set; } = 24;
    public int ResultPollSeconds { get; set; } = 1;
    public int CalculationTimeoutMinutes { get; set; } = 10;
}
