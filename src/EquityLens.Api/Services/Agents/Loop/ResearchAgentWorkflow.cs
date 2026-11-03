using System.Text.Json;
using EquityLens.Api.Contracts.Agents;
using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Services.Agents.Loop;

public static class ResearchAgentNodes
{
    public const string Loop = "ResearchAgentLoop";
    public const string OutputGate = "ResearchOutputGate";
    public const string Route = "RouteResearchAgentQuality";
    public const string Finalize = "FinalizeResearchAgent";
}

/// <summary>研究外框在建立時固定；僅工具決策由迴圈內的 LLM 決定。</summary>
public sealed class ResearchAgentWorkflowDefinitionProvider : IAgentWorkflowDefinitionProvider
{
    public string WorkflowType => AgentWorkflowTypes.ResearchAgent;
    public static readonly (string Key, string Type, bool Revise)[] Steps =
    [
        ("validate", ResearchInvestigationNodeTypes.Validate, false),
        ("detectIntent", ResearchInvestigationNodeTypes.DetectIntent, false),
        ("researchAgent", ResearchAgentNodes.Loop, false),
        ("qualityGate", QualityGateNodeHandler.Type, false),
        ("reviseAgent", ResearchAgentNodes.Loop, true),
        ("reviseQualityGate", QualityGateNodeHandler.Type, true),
        ("finalizeResearch", ResearchAgentNodes.Finalize, false)
    ];
    public AgentRun CreateRun(Guid userId, Guid sourceRunId) => CreateRun(userId, sourceRunId, new("", ""));
    public string CreateInitialBlackboardJson(Guid sourceRunId) => CreateInitialBlackboardJson(sourceRunId, new("", ""));
    public string CreateInitialBlackboardJson(Guid sourceRunId, ResearchAskRequest request, InvestmentResearchRoutingContext? routingContext = null) => new JsonObject
    {
        ["blackboardVersion"] = 0,
        [AgentBlackboardKeys.ResearchRunId] = sourceRunId,
        [AgentBlackboardKeys.Ticker] = request.Ticker.Trim().ToUpperInvariant(),
        [AgentBlackboardKeys.Question] = request.Question,
        [AgentBlackboardKeys.ResearchRequest] = JsonSerializer.SerializeToNode(request, AgentNodeJson.SerializerOptions),
        [AgentBlackboardKeys.Answer] = null,
        [AgentBlackboardKeys.Citations] = new JsonArray(),
        [AgentBlackboardKeys.Candidates] = new JsonArray(),
        [AgentBlackboardKeys.Steps] = new JsonArray(),
        [AgentBlackboardKeys.LeadSkill] = routingContext?.LeadSkill,
        [AgentBlackboardKeys.RoutingContext] = JsonSerializer.SerializeToNode(routingContext, AgentNodeJson.SerializerOptions)
    }.ToJsonString(AgentNodeJson.SerializerOptions);
    public AgentRun CreateRevisionRun(Guid userId, Guid researchRunId, AgentRun parent, ResearchRun parentResearch, Guid feedbackId, string comment)
    {
        var parentBoard = AgentNodeJson.ParseBlackboard(parent.BlackboardJson);
        var request = parentBoard[AgentBlackboardKeys.ResearchRequest]?.Deserialize<ResearchAskRequest>(AgentNodeJson.SerializerOptions)
            ?? new(parentResearch.Ticker, parentResearch.Question,
                Enum.TryParse<RetrievalMode>(parentResearch.RetrievalMode, true, out var retrieval) ? retrieval : null,
                parentResearch.DocumentType,
                Enum.TryParse<SourcePolicy>(parentResearch.SourcePolicy, true, out var policy) ? policy : SourcePolicy.Auto,
                parentResearch.TopK, parentResearch.Temperature);
        var run = CreateRun(userId, researchRunId, request);
        run.ParentAgentRunId = parent.Id;
        var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        foreach (var key in new[] { "researchAgentLedger", "researchAgentLocalSearchCompleted", AgentBlackboardKeys.ResearchIntent,
            AgentBlackboardKeys.CriticReview, "outputGate", AgentBlackboardKeys.LeadSkill, AgentBlackboardKeys.RoutingContext })
            if (parentBoard[key] is { } value) board[key] = value.DeepClone();
        board[AgentBlackboardKeys.ParentAgentRunId] = parent.Id;
        board[AgentBlackboardKeys.ParentResearchRunId] = parentResearch.Id;
        board[AgentBlackboardKeys.FeedbackId] = feedbackId;
        board[AgentBlackboardKeys.FeedbackComment] = comment;
        board[AgentBlackboardKeys.OriginalAnswer] = parentResearch.Answer;
        board[AgentBlackboardKeys.Answer] = parentResearch.Answer;
        run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);
        run.InputJson = AgentNodeJson.Serialize(new { request, revisionSeed = board });
        run.Nodes = run.Nodes.Where(n => n.NodeKey is "reviseAgent" or "reviseQualityGate" or "finalizeResearch").ToList();
        var definition = JsonNode.Parse(run.WorkflowDefinitionJson)!.AsObject();
        definition["mode"] = "Revision";
        definition["nodes"] = new JsonArray(definition["nodes"]!.AsArray().Where(n => run.Nodes.Any(x => x.NodeKey == n!["id"]!.GetValue<string>()))
            .Select(n => { var copy = n!.DeepClone().AsObject(); copy.Remove("condition"); return (JsonNode)copy; }).ToArray());
        definition["edges"] = new JsonArray(run.Nodes.Zip(run.Nodes.Skip(1), (a, b) => (JsonNode)new JsonObject { ["from"] = a.NodeKey, ["to"] = b.NodeKey }).ToArray());
        run.WorkflowDefinitionJson = definition.ToJsonString(AgentNodeJson.SerializerOptions);
        return run;
    }

    public AgentRun CreateRun(Guid userId, Guid sourceRunId, ResearchAskRequest request, InvestmentResearchRoutingContext? routingContext = null)
    {
        var run = new AgentRun
        {
            Id = Guid.NewGuid(), UserId = userId, ResearchRunId = sourceRunId,
            WorkflowType = WorkflowType, AgentType = AgentTypes.Research, Status = AgentRunStatuses.Pending,
            InputJson = AgentNodeJson.Serialize(new { request, routingContext }),
            BlackboardJson = CreateInitialBlackboardJson(sourceRunId, request, routingContext),
            CreatedAtUtc = DateTime.UtcNow, EnableBlackboardSnapshots = true
        };
        run.Nodes = Steps.Select(x => new AgentRunNode { Id = Guid.NewGuid(), AgentRunId = run.Id,
            NodeKey = x.Key, NodeType = x.Type, Status = AgentNodeStatuses.Pending }).ToList();
        run.WorkflowDefinitionJson = new JsonObject
        {
            ["workflowType"] = WorkflowType, ["version"] = 2, ["orchestrationMode"] = "Static",
            ["nodes"] = new JsonArray(Steps.Select(x => (JsonNode)new JsonObject
            {
                ["id"] = x.Key, ["type"] = x.Type, ["required"] = true,
                ["plannedArguments"] = new JsonObject { ["mode"] = x.Revise ? "Revise" : "Research" },
                ["condition"] = x.Revise ? new JsonObject { ["path"] = "qualityRoute.decision", ["equals"] = "Revise" } : null,
                ["executionPolicy"] = new JsonObject { ["timeoutSeconds"] = x.Type == ResearchAgentNodes.Loop ? 300 : 120, ["maxRetryCount"] = 0 }
            }).ToArray()),
            ["edges"] = new JsonArray(Steps.Zip(Steps.Skip(1), (a, b) => (JsonNode)new JsonObject { ["from"] = a.Key, ["to"] = b.Key }).ToArray())
        }.ToJsonString(AgentNodeJson.SerializerOptions);
        return run;
    }
}

public sealed class ResearchAgentPolicyEvaluator : IWorkflowPolicyEvaluator
{
    public string WorkflowType => AgentWorkflowTypes.ResearchAgent;
    public WorkflowPolicyDecision Evaluate(WorkflowPolicyContext context) => new ResearchInvestigationPolicyEvaluator()
        .Evaluate(context with { WorkflowType = AgentWorkflowTypes.ResearchInvestigation });
}
