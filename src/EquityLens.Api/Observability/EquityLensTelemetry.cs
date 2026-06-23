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

    public static void MarkError(Activity? activity, Exception exception)
    {
        activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
        activity?.SetTag("error.type", exception.GetType().FullName);
    }
}
