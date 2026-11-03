using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Ai;

namespace EquityLens.Api.Services.Chat;

public static class ConversationActions
{
    public const string DirectResponse = "DirectResponse";
    public const string AskClarification = "AskClarification";
    public const string RouteWorkflow = "RouteWorkflow";
    public const string RevisePreviousRun = "RevisePreviousRun";
    public const string RejectUnsafe = "RejectUnsafe";
    public const string ResetContext = "ResetContext";
    public static readonly IReadOnlySet<string> All = new HashSet<string>([
        DirectResponse, AskClarification, RouteWorkflow, RevisePreviousRun, RejectUnsafe, ResetContext
    ], StringComparer.Ordinal);
}

public sealed record ConversationPortfolioOption(Guid Id, string Name);

public sealed record ConversationAgentInput(
    string Message,
    JsonObject Context,
    IReadOnlyList<ChatMessage> History,
    IReadOnlyList<ConversationPortfolioOption> Portfolios);

public sealed record ConversationAgentDecision(
    string Action,
    string Response,
    string? StandaloneQuery,
    string ReasonCode,
    string Confidence,
    JsonObject ContextPatch,
    string Summary,
    string Model,
    string Provider,
    int PromptTokens,
    int CompletionTokens,
    long DurationMs, EquityLens.Api.Services.Agents.InvestmentResearchRouteDecision? Route = null);

public interface IConversationAgent
{
    Task<ConversationAgentDecision> DecideAsync(ConversationAgentInput input, CancellationToken cancellationToken = default);
}

