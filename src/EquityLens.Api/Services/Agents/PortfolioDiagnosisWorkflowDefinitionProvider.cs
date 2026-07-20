using System.Text.Json.Nodes;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Services.Agents;

public sealed class PortfolioDiagnosisWorkflowDefinitionProvider : IAgentWorkflowDefinitionProvider
{
    public string WorkflowType => AgentWorkflowTypes.PortfolioDiagnosis;

    public AgentRun CreateRun(Guid userId, Guid portfolioId) =>
        CreateRun(userId, portfolioId, DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-1), DateOnly.FromDateTime(DateTime.UtcNow));

    public string CreateInitialBlackboardJson(Guid portfolioId) =>
        AgentBlackboardContracts.CreateInitialPortfolioDiagnosisBlackboard(portfolioId, DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-1), DateOnly.FromDateTime(DateTime.UtcNow)).ToJsonString(AgentNodeJson.SerializerOptions);

    public AgentRun CreateRun(Guid userId, Guid portfolioId, DateOnly from, DateOnly to) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, WorkflowType = AgentWorkflowTypes.PortfolioDiagnosis,
        AgentType = AgentTypes.Portfolio, Status = AgentRunStatuses.Pending,
        InputJson = AgentNodeJson.Serialize(new { portfolioId, from, to }),
        BlackboardJson = AgentBlackboardContracts.CreateInitialPortfolioDiagnosisBlackboard(portfolioId, from, to).ToJsonString(AgentNodeJson.SerializerOptions),
        WorkflowDefinitionJson = Definition().ToJsonString(AgentNodeJson.SerializerOptions),
        CreatedAtUtc = DateTime.UtcNow,
        Nodes = [
            Node(PortfolioDiagnosisNodeKeys.LoadContext, PortfolioDiagnosisNodeTypes.LoadContext),
            Node(PortfolioDiagnosisNodeKeys.CalculateAttribution, PortfolioDiagnosisNodeTypes.CalculateAttribution),
            Node(PortfolioDiagnosisNodeKeys.LoadRiskProfile, PortfolioDiagnosisNodeTypes.LoadRiskProfile),
            Node(PortfolioDiagnosisNodeKeys.PrioritizeRiskAnalyses, PortfolioDiagnosisNodeTypes.PrioritizeRiskAnalyses),
            Node(PortfolioDiagnosisNodeKeys.BuildEvidencePacket, PortfolioDiagnosisNodeTypes.BuildEvidencePacket),
            Node(PortfolioDiagnosisNodeKeys.DraftDiagnosis, PortfolioDiagnosisNodeTypes.DraftDiagnosis),
            Node(PortfolioDiagnosisNodeKeys.FinalizeDiagnosis, PortfolioDiagnosisNodeTypes.FinalizeDiagnosis)]
    };

    private static AgentRunNode Node(string key, string type) => new() { Id = Guid.NewGuid(), NodeKey = key, NodeType = type, Status = AgentNodeStatuses.Pending };

    private static JsonObject Definition() => new()
    {
        ["workflowType"] = AgentWorkflowTypes.PortfolioDiagnosis,
        ["version"] = PortfolioDiagnosisWorkflow.Version,
        ["nodes"] = new JsonArray
        {
            N(PortfolioDiagnosisNodeKeys.LoadContext, PortfolioDiagnosisNodeTypes.LoadContext), N(PortfolioDiagnosisNodeKeys.CalculateAttribution, PortfolioDiagnosisNodeTypes.CalculateAttribution), N(PortfolioDiagnosisNodeKeys.LoadRiskProfile, PortfolioDiagnosisNodeTypes.LoadRiskProfile), N(PortfolioDiagnosisNodeKeys.PrioritizeRiskAnalyses, PortfolioDiagnosisNodeTypes.PrioritizeRiskAnalyses), N(PortfolioDiagnosisNodeKeys.BuildEvidencePacket, PortfolioDiagnosisNodeTypes.BuildEvidencePacket), N(PortfolioDiagnosisNodeKeys.DraftDiagnosis, PortfolioDiagnosisNodeTypes.DraftDiagnosis), N(PortfolioDiagnosisNodeKeys.FinalizeDiagnosis, PortfolioDiagnosisNodeTypes.FinalizeDiagnosis)
        },
        ["edges"] = new JsonArray
        {
            E(PortfolioDiagnosisNodeKeys.LoadContext, PortfolioDiagnosisNodeKeys.CalculateAttribution), E(PortfolioDiagnosisNodeKeys.CalculateAttribution, PortfolioDiagnosisNodeKeys.LoadRiskProfile), E(PortfolioDiagnosisNodeKeys.LoadRiskProfile, PortfolioDiagnosisNodeKeys.PrioritizeRiskAnalyses), E(PortfolioDiagnosisNodeKeys.PrioritizeRiskAnalyses, PortfolioDiagnosisNodeKeys.BuildEvidencePacket), E(PortfolioDiagnosisNodeKeys.BuildEvidencePacket, PortfolioDiagnosisNodeKeys.DraftDiagnosis), E(PortfolioDiagnosisNodeKeys.DraftDiagnosis, PortfolioDiagnosisNodeKeys.FinalizeDiagnosis)
        }
    };

    private static JsonObject N(string id, string type) => new() { ["id"] = id, ["type"] = type, ["required"] = true };
    private static JsonObject E(string from, string to) => new() { ["from"] = from, ["to"] = to };
}
