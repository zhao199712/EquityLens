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
        ("outputGate", ResearchAgentNodes.OutputGate, false),
        ("buildEvidencePacket", CriticReviewNodeTypes.BuildEvidencePacket, false),
        ("checkEvidence", CriticReviewNodeTypes.CheckEvidence, false),
        ("critiqueAnswer", CriticReviewNodeTypes.CritiqueAnswer, false),
        ("finalizeCriticReport", CriticReviewNodeTypes.FinalizeCriticReport, false),
        ("routeQuality", ResearchAgentNodes.Route, false),
        ("reviseAgent", ResearchAgentNodes.Loop, true),
        ("reviseOutputGate", ResearchAgentNodes.OutputGate, true),
        ("reviseEvidencePacket", CriticReviewNodeTypes.BuildEvidencePacket, true),
        ("reviseCheckEvidence", CriticReviewNodeTypes.CheckEvidence, true),
        ("reviseCritiqueAnswer", CriticReviewNodeTypes.CritiqueAnswer, true),
        ("reviseCriticReport", CriticReviewNodeTypes.FinalizeCriticReport, true),
        ("finalizeResearch", ResearchAgentNodes.Finalize, false)
    ];
    public AgentRun CreateRun(Guid userId, Guid sourceRunId) => CreateRun(userId, sourceRunId, new("", ""));
    public string CreateInitialBlackboardJson(Guid sourceRunId) => new ResearchInvestigationWorkflowDefinitionProvider().CreateInitialBlackboardJson(sourceRunId);
    public AgentRun CreateRun(Guid userId, Guid sourceRunId, ResearchAskRequest request, InvestmentResearchRoutingContext? routingContext = null)
    {
        var run = new ResearchInvestigationWorkflowDefinitionProvider().CreateRun(userId, sourceRunId, request, routingContext);
        run.WorkflowType = WorkflowType;
        run.Nodes = Steps.Select(x => new AgentRunNode { Id = Guid.NewGuid(), AgentRunId = run.Id,
            NodeKey = x.Key, NodeType = x.Type, Status = AgentNodeStatuses.Pending }).ToList();
        run.WorkflowDefinitionJson = new JsonObject
        {
            ["workflowType"] = WorkflowType, ["version"] = 1, ["orchestrationMode"] = "Static",
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
