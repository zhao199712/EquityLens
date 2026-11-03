using System.Text.Json.Nodes;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Services.Agents;

public interface IAgentWorkflowDefinitionProvider
{
    string WorkflowType { get; }

    AgentRun CreateRun(Guid userId, Guid sourceRunId);

    string CreateInitialBlackboardJson(Guid sourceRunId);
}

public sealed class CriticReviewWorkflowDefinitionProvider : IAgentWorkflowDefinitionProvider
{
    public string WorkflowType => AgentWorkflowTypes.CriticReview;

    public AgentRun CreateRun(Guid userId, Guid researchRunId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        WorkflowType = AgentWorkflowTypes.CriticReview,
        AgentType = AgentTypes.Critic,
        Status = AgentRunStatuses.Pending,
        InputJson = AgentNodeJson.Serialize(new { researchRunId }),
        BlackboardJson = CreateInitialBlackboardJson(researchRunId),
        WorkflowDefinitionJson = CreateWorkflowDefinition().ToJsonString(AgentNodeJson.SerializerOptions),
        CreatedAtUtc = DateTime.UtcNow,
        Nodes = CreateNodes()
    };


    public string CreateInitialBlackboardJson(Guid researchRunId) =>
        AgentBlackboardContracts.CreateInitialCriticReviewBlackboard(researchRunId).ToJsonString(AgentNodeJson.SerializerOptions);

    private static List<AgentRunNode> CreateNodes() =>
    [
        new AgentRunNode { Id = Guid.NewGuid(), NodeKey = CriticReviewNodeKeys.LoadResearchRun, NodeType = CriticReviewNodeTypes.LoadResearchRun, Status = AgentNodeStatuses.Pending },
        new AgentRunNode { Id = Guid.NewGuid(), NodeKey = "qualityGate", NodeType = Loop.QualityGateNodeHandler.Type, Status = AgentNodeStatuses.Pending },
        new AgentRunNode { Id = Guid.NewGuid(), NodeKey = CriticReviewNodeKeys.FinalizeCriticReport, NodeType = CriticReviewNodeTypes.FinalizeCriticReport, Status = AgentNodeStatuses.Pending }
    ];

    private static JsonObject CreateWorkflowDefinition() => new()
    {
        ["workflowType"] = AgentWorkflowTypes.CriticReview,
        ["version"] = CriticReviewWorkflow.Version,
        ["nodes"] = new JsonArray
        {
            new JsonObject { ["id"] = CriticReviewNodeKeys.LoadResearchRun, ["type"] = CriticReviewNodeTypes.LoadResearchRun, ["required"] = true },
            new JsonObject { ["id"] = "qualityGate", ["type"] = Loop.QualityGateNodeHandler.Type, ["required"] = true },
            new JsonObject { ["id"] = CriticReviewNodeKeys.FinalizeCriticReport, ["type"] = CriticReviewNodeTypes.FinalizeCriticReport, ["required"] = true }
        },
        ["edges"] = new JsonArray
        {
            new JsonObject { ["from"] = CriticReviewNodeKeys.LoadResearchRun, ["to"] = "qualityGate" },
            new JsonObject { ["from"] = "qualityGate", ["to"] = CriticReviewNodeKeys.FinalizeCriticReport }
        }
    };
}
