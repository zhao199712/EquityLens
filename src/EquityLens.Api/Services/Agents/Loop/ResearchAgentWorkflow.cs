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
