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
        new AgentRunNode { Id = Guid.NewGuid(), NodeKey = CriticReviewNodeKeys.BuildEvidencePacket, NodeType = CriticReviewNodeTypes.BuildEvidencePacket, Status = AgentNodeStatuses.Pending },
        new AgentRunNode { Id = Guid.NewGuid(), NodeKey = CriticReviewNodeKeys.CheckEvidence, NodeType = CriticReviewNodeTypes.CheckEvidence, Status = AgentNodeStatuses.Pending },
        new AgentRunNode { Id = Guid.NewGuid(), NodeKey = CriticReviewNodeKeys.CritiqueAnswer, NodeType = CriticReviewNodeTypes.CritiqueAnswer, Status = AgentNodeStatuses.Pending },
        new AgentRunNode { Id = Guid.NewGuid(), NodeKey = CriticReviewNodeKeys.FinalizeCriticReport, NodeType = CriticReviewNodeTypes.FinalizeCriticReport, Status = AgentNodeStatuses.Pending }
    ];

    private static JsonObject CreateWorkflowDefinition() => new()
    {
        ["workflowType"] = AgentWorkflowTypes.CriticReview,
        ["version"] = CriticReviewWorkflow.Version,
        ["nodes"] = new JsonArray
        {
            new JsonObject { ["id"] = CriticReviewNodeKeys.LoadResearchRun, ["type"] = CriticReviewNodeTypes.LoadResearchRun, ["required"] = true },
            new JsonObject { ["id"] = CriticReviewNodeKeys.BuildEvidencePacket, ["type"] = CriticReviewNodeTypes.BuildEvidencePacket, ["required"] = true },
            new JsonObject { ["id"] = CriticReviewNodeKeys.CheckEvidence, ["type"] = CriticReviewNodeTypes.CheckEvidence, ["required"] = true },
            new JsonObject { ["id"] = CriticReviewNodeKeys.CritiqueAnswer, ["type"] = CriticReviewNodeTypes.CritiqueAnswer, ["required"] = true },
            new JsonObject { ["id"] = CriticReviewNodeKeys.FinalizeCriticReport, ["type"] = CriticReviewNodeTypes.FinalizeCriticReport, ["required"] = true }
        },
        ["edges"] = new JsonArray
        {
            new JsonObject { ["from"] = CriticReviewNodeKeys.LoadResearchRun, ["to"] = CriticReviewNodeKeys.BuildEvidencePacket },
            new JsonObject { ["from"] = CriticReviewNodeKeys.BuildEvidencePacket, ["to"] = CriticReviewNodeKeys.CheckEvidence },
            new JsonObject { ["from"] = CriticReviewNodeKeys.CheckEvidence, ["to"] = CriticReviewNodeKeys.CritiqueAnswer },
            new JsonObject { ["from"] = CriticReviewNodeKeys.CritiqueAnswer, ["to"] = CriticReviewNodeKeys.FinalizeCriticReport }
        }
    };
}

public sealed class DraftRevisionWorkflowDefinitionProvider : IAgentWorkflowDefinitionProvider
{
    public string WorkflowType => AgentWorkflowTypes.DraftRevision;

    public AgentRun CreateRun(Guid userId, Guid criticReviewRunId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        WorkflowType = AgentWorkflowTypes.DraftRevision,
        AgentType = AgentTypes.Draft,
        Status = AgentRunStatuses.Pending,
        InputJson = AgentNodeJson.Serialize(new { criticReviewRunId }),
        BlackboardJson = CreateInitialBlackboardJson(criticReviewRunId),
        WorkflowDefinitionJson = CreateWorkflowDefinition().ToJsonString(AgentNodeJson.SerializerOptions),
        CreatedAtUtc = DateTime.UtcNow,
        Nodes = CreateNodes()
    };

    public string CreateInitialBlackboardJson(Guid criticReviewRunId) =>
        AgentBlackboardContracts.CreateInitialDraftRevisionBlackboard(criticReviewRunId).ToJsonString(AgentNodeJson.SerializerOptions);

    private static List<AgentRunNode> CreateNodes() =>
    [
        new AgentRunNode { Id = Guid.NewGuid(), NodeKey = DraftRevisionNodeKeys.LoadCriticReviewRun, NodeType = DraftRevisionNodeTypes.LoadCriticReviewRun, Status = AgentNodeStatuses.Pending },
        new AgentRunNode { Id = Guid.NewGuid(), NodeKey = DraftRevisionNodeKeys.DraftRevisedAnswer, NodeType = DraftRevisionNodeTypes.DraftRevisedAnswer, Status = AgentNodeStatuses.Pending },
        new AgentRunNode { Id = Guid.NewGuid(), NodeKey = DraftRevisionNodeKeys.FinalizeRevision, NodeType = DraftRevisionNodeTypes.FinalizeRevision, Status = AgentNodeStatuses.Pending }
    ];

    private static JsonObject CreateWorkflowDefinition() => new()
    {
        ["workflowType"] = AgentWorkflowTypes.DraftRevision,
        ["version"] = DraftRevisionWorkflow.Version,
        ["nodes"] = new JsonArray
        {
            new JsonObject { ["id"] = DraftRevisionNodeKeys.LoadCriticReviewRun, ["type"] = DraftRevisionNodeTypes.LoadCriticReviewRun, ["required"] = true },
            new JsonObject { ["id"] = DraftRevisionNodeKeys.DraftRevisedAnswer, ["type"] = DraftRevisionNodeTypes.DraftRevisedAnswer, ["required"] = true },
            new JsonObject { ["id"] = DraftRevisionNodeKeys.FinalizeRevision, ["type"] = DraftRevisionNodeTypes.FinalizeRevision, ["required"] = true }
        },
        ["edges"] = new JsonArray
        {
            new JsonObject { ["from"] = DraftRevisionNodeKeys.LoadCriticReviewRun, ["to"] = DraftRevisionNodeKeys.DraftRevisedAnswer },
            new JsonObject { ["from"] = DraftRevisionNodeKeys.DraftRevisedAnswer, ["to"] = DraftRevisionNodeKeys.FinalizeRevision }
        }
    };
}
