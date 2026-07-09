namespace EquityLens.Api.Services.Agents;

public sealed class AgentRunQueueOptions
{
    public const string SectionName = "AgentRunQueue";

    public string StreamKey { get; set; } = "equitylens:agent-runs:stream";

    public string ConsumerGroupName { get; set; } = "agent-run-workers";

    public int PendingMinIdleSeconds { get; set; } = 300;
}
