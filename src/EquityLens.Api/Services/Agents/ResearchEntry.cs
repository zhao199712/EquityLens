using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Agents;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Ai;
using EquityLens.Api.Services.Ai.Retrieval;
using EquityLens.Api.Services.Research;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace EquityLens.Api.Services.Agents;

public sealed record ResearchAgentInput(
    ResearchAskRequest Request,
    InvestmentResearchRoutingContext? RoutingContext);

public static partial class ResearchQuestionContext
{
    private static readonly string[] FreshnessTerms = ["今天", "昨日", "昨天", "本週", "本月", "近期", "最近", "最新", "為什麼跌", "為甚麼跌", "為什麼漲", "為甚麼漲", "股價異動", "新聞", "today", "yesterday", "latest", "current", "price move", "news"];

    public static bool IsFreshnessSensitive(string question) =>
        FreshnessTerms.Any(x => question.Contains(x, StringComparison.OrdinalIgnoreCase))
        || DatePattern().IsMatch(question);

    [GeneratedRegex(@"(?:\d{4}[/-])?\d{1,2}[/-]\d{1,2}", RegexOptions.CultureInvariant)]
    private static partial Regex DatePattern();
}

public static class ResearchInputReader
{
    public static ResearchAgentInput ParseInput(string inputJson)
    {
        var envelope = JsonSerializer.Deserialize<ResearchAgentInput>(inputJson, AgentNodeJson.SerializerOptions);
        if (envelope?.Request is not null) return envelope;
        var legacy = JsonSerializer.Deserialize<ResearchAskRequest>(inputJson, AgentNodeJson.SerializerOptions)
            ?? throw new InvalidOperationException("Research input is invalid.");
        return new(legacy, null);
    }

}

internal static class ResearchBlackboard
{
    public static JsonObject Parse(AgentRun run) => AgentNodeJson.ParseBlackboard(run.BlackboardJson);
    public static T Required<T>(JsonObject board, string key)
    {
        var node = board[key] ?? throw new AgentNodeException("blackboard_value_missing", AgentNodeErrorCategories.ValidationFailure, $"Blackboard key '{key}' is missing.");
        return node.Deserialize<T>(AgentNodeJson.SerializerOptions)!;
    }
    public static void Commit(AgentNodeExecutionContext context, JsonObject board, object output)
    {
        context.Node.OutputJson = AgentNodeJson.Serialize(output);
        context.Run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);
    }
}

public sealed class ValidateResearchRequestNodeHandler(IResearchPreflightService preflight) : IAgentNodeHandler
{
    public string NodeType => ResearchEntryNodeTypes.Validate;
    public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = ResearchBlackboard.Parse(context.Run); var request = ResearchBlackboard.Required<ResearchAskRequest>(board, AgentBlackboardKeys.ResearchRequest);
        if (string.IsNullOrWhiteSpace(request.Ticker) || string.IsNullOrWhiteSpace(request.Question)) throw new AgentNodeException("invalid_research_request", AgentNodeErrorCategories.ValidationFailure, "Ticker and question are required.");
        var result = await preflight.ValidateAskAsync(request, cancellationToken);
        if (!result.IsSuccess) throw new AgentNodeException(result.ErrorCode ?? "preflight_failed", AgentNodeErrorCategories.ValidationFailure, result.ErrorMessage ?? "Research preflight failed.");
        board[AgentBlackboardKeys.Ticker] = result.Value!.Ticker; ResearchBlackboard.Commit(context, board, result.Value);
    }
}

public sealed class DetectResearchIntentNodeHandler(IIntentDetector detector) : IAgentNodeHandler
{
    public string NodeType => ResearchEntryNodeTypes.DetectIntent;
    public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = ResearchBlackboard.Parse(context.Run); var result = detector.Detect(board[AgentBlackboardKeys.Question]!.GetValue<string>());
        board[AgentBlackboardKeys.ResearchIntent] = JsonSerializer.SerializeToNode(result, AgentNodeJson.SerializerOptions); ResearchBlackboard.Commit(context, board, result); return Task.CompletedTask;
    }
}

