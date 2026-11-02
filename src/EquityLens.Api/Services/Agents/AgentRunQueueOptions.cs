namespace EquityLens.Api.Services.Agents;

public sealed class AgentRunQueueOptions
{
    public const string SectionName = "AgentRunQueue";

    public string StreamKey { get; set; } = "equitylens:agent-runs:stream";

    public string ConsumerGroupName { get; set; } = "agent-run-workers";

    /// <summary>重新認領間隔預設大於五分鐘租約；正確性仍由執行結果與租約檢查保證。</summary>
    public int PendingMinIdleSeconds { get; set; } = 420;
}
