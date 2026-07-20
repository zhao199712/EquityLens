using System.Text.Json.Nodes;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Services.Agents;

public sealed class EvidenceRemediationWorkflowDefinitionProvider : IAgentWorkflowDefinitionProvider
{
    private static readonly (string Key, string Template, string Type, int Iteration, bool Conditional)[] Steps =
    [
        (EvidenceRemediationNodeKeys.LoadContext, EvidenceRemediationNodeKeys.LoadContext, EvidenceRemediationNodeTypes.LoadContext, 0, false),
        (EvidenceRemediationNodeKeys.ExtractClaims, EvidenceRemediationNodeKeys.ExtractClaims, EvidenceRemediationNodeTypes.ExtractClaims, 0, false),
        ("planEvidenceRetrieval:1", EvidenceRemediationNodeKeys.PlanRetrieval, EvidenceRemediationNodeTypes.PlanRetrieval, 1, false),
        ("retrieveRemediationEvidence:1", EvidenceRemediationNodeKeys.RetrieveEvidence, EvidenceRemediationNodeTypes.RetrieveEvidence, 1, false),
        ("assessClaimSupport:1", EvidenceRemediationNodeKeys.AssessSupport, EvidenceRemediationNodeTypes.AssessSupport, 1, false),
        ("validateEvidenceMappings:1", EvidenceRemediationNodeKeys.ValidateMappings, EvidenceRemediationNodeTypes.ValidateMappings, 1, false),
        ("routeEvidenceRemediation:1", EvidenceRemediationNodeKeys.Route, EvidenceRemediationNodeTypes.Route, 1, false),
        ("planEvidenceRetrieval:2", EvidenceRemediationNodeKeys.PlanRetrieval, EvidenceRemediationNodeTypes.PlanRetrieval, 2, true),
        ("retrieveRemediationEvidence:2", EvidenceRemediationNodeKeys.RetrieveEvidence, EvidenceRemediationNodeTypes.RetrieveEvidence, 2, true),
        ("assessClaimSupport:2", EvidenceRemediationNodeKeys.AssessSupport, EvidenceRemediationNodeTypes.AssessSupport, 2, true),
        ("validateEvidenceMappings:2", EvidenceRemediationNodeKeys.ValidateMappings, EvidenceRemediationNodeTypes.ValidateMappings, 2, true),
        ("routeEvidenceRemediation:2", EvidenceRemediationNodeKeys.Route, EvidenceRemediationNodeTypes.Route, 2, true),
        (EvidenceRemediationNodeKeys.BuildPacket, EvidenceRemediationNodeKeys.BuildPacket, EvidenceRemediationNodeTypes.BuildPacket, 0, false),
        (EvidenceRemediationNodeKeys.DraftRevision, EvidenceRemediationNodeKeys.DraftRevision, EvidenceRemediationNodeTypes.DraftRevision, 0, false),
        (EvidenceRemediationNodeKeys.Finalize, EvidenceRemediationNodeKeys.Finalize, EvidenceRemediationNodeTypes.Finalize, 0, false)
    ];

    public string WorkflowType => AgentWorkflowTypes.EvidenceRemediation;

    public AgentRun CreateRun(Guid userId, Guid criticReviewRunId) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, WorkflowType = WorkflowType, AgentType = AgentTypes.Research,
        Status = AgentRunStatuses.Pending, InputJson = AgentNodeJson.Serialize(new { criticReviewRunId }),
        BlackboardJson = CreateInitialBlackboardJson(criticReviewRunId),
        WorkflowDefinitionJson = CreateDefinition().ToJsonString(AgentNodeJson.SerializerOptions), CreatedAtUtc = DateTime.UtcNow,
        EnableBlackboardSnapshots = true,
        Nodes = Steps.Select(step => new AgentRunNode { Id = Guid.NewGuid(), NodeKey = step.Key, TemplateNodeKey = step.Template, Iteration = step.Iteration, NodeType = step.Type, Status = AgentNodeStatuses.Pending }).ToList()
    };

    public string CreateInitialBlackboardJson(Guid criticReviewRunId) =>
        AgentBlackboardContracts.CreateInitialEvidenceRemediationBlackboard(criticReviewRunId).ToJsonString(AgentNodeJson.SerializerOptions);

    private static JsonObject CreateDefinition() => new()
    {
        ["workflowType"] = AgentWorkflowTypes.EvidenceRemediation,
        ["version"] = EvidenceRemediationWorkflow.Version,
        ["orchestrationMode"] = "Stateful",
        ["maxIterations"] = EvidenceRemediationWorkflow.MaxIterations,
        ["nodes"] = new JsonArray(Steps.Select(step => (JsonNode)new JsonObject { ["id"] = step.Key, ["templateNodeKey"] = step.Template, ["type"] = step.Type, ["iteration"] = step.Iteration, ["required"] = true, ["condition"] = step.Conditional ? new JsonObject { ["path"] = AgentBlackboardKeys.RouteDecision, ["equals"] = "InsufficientEvidence" } : null }).ToArray()),
        ["edges"] = new JsonArray(Steps.Zip(Steps.Skip(1), (from, to) => (JsonNode)new JsonObject { ["from"] = from.Key, ["to"] = to.Key }).ToArray())
    };
}
