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
    public static readonly Counter<long> AgentApprovalDecisions = Meter.CreateCounter<long>(
        "equitylens.agent.approval.decisions",
        description: "Number of agent approval requests and decisions.");
    public static readonly Histogram<double> AgentApprovalWaitDuration = Meter.CreateHistogram<double>(
        "equitylens.agent.approval.wait.duration",
        unit: "ms",
        description: "Time agent nodes spend waiting for human approval.");

    // Risk analysis. Tags are deliberately limited to operation/model/confidence/outcome;
    // portfolio identifiers, securities and monetary amounts must not enter metrics.
    public static readonly Counter<long> RiskOperations = Meter.CreateCounter<long>(
        "equitylens.risk.operations",
        description: "Number of risk-analysis operations.");
    public static readonly Histogram<double> RiskOperationDuration = Meter.CreateHistogram<double>(
        "equitylens.risk.operation.duration",
        unit: "ms",
        description: "End-to-end duration of risk-analysis operations.");
    public static readonly Histogram<double> RiskStageDuration = Meter.CreateHistogram<double>(
        "equitylens.risk.stage.duration",
        unit: "ms",
        description: "Duration of a risk-analysis calculation stage.");

    public static void MarkError(Activity? activity, Exception exception)
    {
        activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
        activity?.SetTag("error.type", exception.GetType().FullName);
    }

    public static Activity? StartRiskOperation(
        string operation,
        string? model = null,
        decimal? confidenceLevel = null,
        int? simulations = null)
    {
        var activity = ActivitySource.StartActivity(operation);
        activity?.SetTag("risk.operation", operation);
        if (!string.IsNullOrWhiteSpace(model)) activity?.SetTag("risk.model", model);
        if (confidenceLevel is not null) activity?.SetTag("risk.confidence_level", (double)confidenceLevel.Value);
        if (simulations is not null) activity?.SetTag("risk.simulations", simulations.Value);
        return activity;
    }

    public static IDisposable StartRiskStage(Activity? parent, string stage)
    {
        var activity = ActivitySource.StartActivity($"risk.portfolio.{stage}");
        activity?.SetTag("risk.stage", stage);
        return new RiskStage(activity, stage);
    }

    public static void CompleteRiskOperation(
        Activity? activity,
        string operation,
        Stopwatch stopwatch,
        bool succeeded,
        string? errorCode = null,
        string? model = null,
        decimal? confidenceLevel = null)
    {
        var outcome = succeeded ? "success" : "failure";
        activity?.SetTag("risk.outcome", outcome);
        if (!succeeded)
        {
            activity?.SetStatus(ActivityStatusCode.Error, errorCode ?? "risk_operation_failed");
            if (!string.IsNullOrWhiteSpace(errorCode)) activity?.SetTag("risk.error.code", errorCode);
        }

        var tags = new TagList { { "risk.operation", operation }, { "risk.outcome", outcome } };
        if (!string.IsNullOrWhiteSpace(model)) tags.Add("risk.model", model);
        if (confidenceLevel is not null) tags.Add("risk.confidence_level", (double)confidenceLevel.Value);
        RiskOperations.Add(1, tags);
        RiskOperationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);
    }

    private sealed class RiskStage : IDisposable
    {
        private readonly Activity? _activity;
        private readonly string _stage;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        public RiskStage(Activity? activity, string stage)
        {
            _activity = activity;
            _stage = stage;
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            RiskStageDuration.Record(_stopwatch.Elapsed.TotalMilliseconds, new TagList { { "risk.stage", _stage } });
            _activity?.Dispose();
        }
    }
}
