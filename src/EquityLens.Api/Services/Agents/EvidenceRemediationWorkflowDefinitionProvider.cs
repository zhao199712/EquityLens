using System.Text.Json.Nodes;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Services.Agents;

public sealed class EvidenceRemediationWorkflowDefinitionProvider : IAgentWorkflowDefinitionProvider
{
    private static readonly (string Key, string Type)[] Steps =
    [
        (EvidenceRemediationNodeKeys.LoadContext, EvidenceRemediationNodeTypes.LoadContext),
        (EvidenceRemediationNodeKeys.PlanRetrieval, EvidenceRemediationNodeTypes.PlanRetrieval),
        (EvidenceRemediationNodeKeys.RetrieveEvidence, EvidenceRemediationNodeTypes.RetrieveEvidence),
        (EvidenceRemediationNodeKeys.ExtractClaims, EvidenceRemediationNodeTypes.ExtractClaims),
        (EvidenceRemediationNodeKeys.AssessSupport, EvidenceRemediationNodeTypes.AssessSupport),
        (EvidenceRemediationNodeKeys.ValidateMappings, EvidenceRemediationNodeTypes.ValidateMappings),
        (EvidenceRemediationNodeKeys.BuildPacket, EvidenceRemediationNodeTypes.BuildPacket),
        (EvidenceRemediationNodeKeys.DraftRevision, EvidenceRemediationNodeTypes.DraftRevision),
        (EvidenceRemediationNodeKeys.Finalize, EvidenceRemediationNodeTypes.Finalize)
    ];

    public string WorkflowType => AgentWorkflowTypes.EvidenceRemediation;

    public AgentRun CreateRun(Guid userId, Guid criticReviewRunId) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, WorkflowType = WorkflowType, AgentType = AgentTypes.Research,
        Status = AgentRunStatuses.Pending, InputJson = AgentNodeJson.Serialize(new { criticReviewRunId }),
        BlackboardJson = CreateInitialBlackboardJson(criticReviewRunId),
        WorkflowDefinitionJson = CreateDefinition().ToJsonString(AgentNodeJson.SerializerOptions), CreatedAtUtc = DateTime.UtcNow,
        Nodes = Steps.Select(step => new AgentRunNode { Id = Guid.NewGuid(), NodeKey = step.Key, NodeType = step.Type, Status = AgentNodeStatuses.Pending }).ToList()
    };

    public string CreateInitialBlackboardJson(Guid criticReviewRunId) =>
        AgentBlackboardContracts.CreateInitialEvidenceRemediationBlackboard(criticReviewRunId).ToJsonString(AgentNodeJson.SerializerOptions);

    private static JsonObject CreateDefinition() => new()
    {
        ["workflowType"] = AgentWorkflowTypes.EvidenceRemediation,
        ["version"] = EvidenceRemediationWorkflow.Version,
        ["nodes"] = new JsonArray(Steps.Select(step => (JsonNode)new JsonObject { ["id"] = step.Key, ["type"] = step.Type, ["required"] = true }).ToArray()),
        ["edges"] = new JsonArray(Steps.Zip(Steps.Skip(1), (from, to) => (JsonNode)new JsonObject { ["from"] = from.Key, ["to"] = to.Key }).ToArray())
    };
}
