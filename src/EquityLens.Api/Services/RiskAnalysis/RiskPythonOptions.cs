namespace EquityLens.Api.Services.RiskAnalysis;

public sealed class RiskPythonOptions
{
    public const string SectionName = "RiskPython";
    public bool ShadowEnabled { get; set; }
    public string JobsStreamKey { get; set; } = "equitylens:risk-python:jobs";
    public string JobsConsumerGroup { get; set; } = "risk-python-workers";
    public string ResultsStreamKey { get; set; } = "equitylens:risk-python:results";
    public string ResultsConsumerGroup { get; set; } = "risk-shadow-result-consumers";
    public string DeadLetterStreamKey { get; set; } = "equitylens:risk-python:dead-letter";
    public string PayloadKeyPrefix { get; set; } = "equitylens:risk-python";
    public string CandidateAlgorithmVersion { get; set; } = "mvewma-fhs-python-v1";
    public int PayloadTtlHours { get; set; } = 24;
    public int ResultPollSeconds { get; set; } = 1;
}
