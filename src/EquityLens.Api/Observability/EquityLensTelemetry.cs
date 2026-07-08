using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace EquityLens.Api.Observability;

public static class EquityLensTelemetry
{
    public const string ActivitySourceName = "EquityLens.Api";
    public const string MeterName = "EquityLens.Api";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);
    public static readonly Counter<long> AskRequests = Meter.CreateCounter<long>(
        "equitylens.research.ask.requests",
        description: "Number of research ask requests.");
    public static readonly Histogram<double> AskDuration = Meter.CreateHistogram<double>(
        "equitylens.research.ask.duration",
        unit: "ms",
        description: "End-to-end research ask duration.");
    public static readonly Counter<long> LlmTokens = Meter.CreateCounter<long>(
        "equitylens.research.llm.tokens",
        unit: "tokens",
        description: "LLM prompt and completion token usage.");

    // Chat
    public static readonly Counter<long> ChatMessages = Meter.CreateCounter<long>(
        "equitylens.chat.messages",
        description: "Number of chat messages sent.");
    public static readonly Histogram<double> ChatLlmDuration = Meter.CreateHistogram<double>(
        "equitylens.chat.llm.duration",
        unit: "ms",
        description: "Gemini API call duration per iteration.");
    public static readonly Counter<long> ChatToolCalls = Meter.CreateCounter<long>(
        "equitylens.chat.tool.calls",
        description: "Number of tool calls in chat.");
    public static readonly Histogram<double> ChatToolDuration = Meter.CreateHistogram<double>(
        "equitylens.chat.tool.duration",
        unit: "ms",
        description: "Tool execution duration.");
    public static readonly Counter<long> ChatSessions = Meter.CreateCounter<long>(
        "equitylens.chat.sessions",
        description: "Number of chat sessions created.");

    // Agent runtime
    public static readonly Counter<long> AgentRunStatusTransitions = Meter.CreateCounter<long>(
        "equitylens.agent.run.status.transitions",
        description: "Number of agent run status transitions.");
    public static readonly Counter<long> AgentNodeStatusTransitions = Meter.CreateCounter<long>(
        "equitylens.agent.node.status.transitions",
        description: "Number of agent node status transitions.");

    public static void MarkError(Activity? activity, Exception exception)
    {
        activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
        activity?.SetTag("error.type", exception.GetType().FullName);
    }
}
