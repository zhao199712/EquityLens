using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Agents;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Services.Agents;

public sealed record PortfolioDiagnosisInput(
    Guid PortfolioId,
    DateOnly From,
    DateOnly To,
    InvestmentResearchRoutingContext? RoutingContext);

public sealed class PortfolioDiagnosisWorkflowDefinitionProvider : IAgentWorkflowDefinitionProvider
{
    public string WorkflowType => AgentWorkflowTypes.PortfolioDiagnosis;

    public AgentRun CreateRun(Guid userId, Guid portfolioId) =>
        CreateRun(userId, portfolioId, DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1), DateOnly.FromDateTime(DateTime.UtcNow));

    public string CreateInitialBlackboardJson(Guid portfolioId) =>
        AgentBlackboardContracts.CreateInitialPortfolioDiagnosisBlackboard(portfolioId, DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1), DateOnly.FromDateTime(DateTime.UtcNow)).ToJsonString(AgentNodeJson.SerializerOptions);

    public AgentRun CreateRun(
        Guid userId,
        Guid portfolioId,
        DateOnly from,
        DateOnly to,
        InvestmentResearchRoutingContext? routingContext = null)
    {
        var run = new AgentRun()
        {
            Id = Guid.NewGuid(), UserId = userId, WorkflowType = AgentWorkflowTypes.PortfolioDiagnosis,
            AgentType = AgentTypes.Portfolio, Status = AgentRunStatuses.Pending,
            InputJson = AgentNodeJson.Serialize(new PortfolioDiagnosisInput(portfolioId, from, to, routingContext)),
            BlackboardJson = CreateInitialBlackboardJson(portfolioId, from, to, routingContext),
            WorkflowDefinitionJson = Definition().ToJsonString(AgentNodeJson.SerializerOptions),
            CreatedAtUtc = DateTime.UtcNow,
            Nodes = Steps.Select(x => Node(x.Key, x.Type)).ToList()
        };
        return run;
    }

    public static PortfolioDiagnosisInput ParseInput(string inputJson) =>
        System.Text.Json.JsonSerializer.Deserialize<PortfolioDiagnosisInput>(inputJson, AgentNodeJson.SerializerOptions)
        ?? throw new InvalidOperationException("PortfolioDiagnosis input is invalid.");

    public static string CreateInitialBlackboardJson(
        Guid portfolioId,
        DateOnly from,
        DateOnly to,
        InvestmentResearchRoutingContext? routingContext)
    {
        var board = AgentBlackboardContracts.CreateInitialPortfolioDiagnosisBlackboard(portfolioId, from, to);
        board[AgentBlackboardKeys.LeadSkill] = routingContext?.LeadSkill;
        board[AgentBlackboardKeys.RoutingContext] = System.Text.Json.JsonSerializer.SerializeToNode(routingContext, AgentNodeJson.SerializerOptions);
        return board.ToJsonString(AgentNodeJson.SerializerOptions);
    }

    private static AgentRunNode Node(string key, string type) => new() { Id = Guid.NewGuid(), NodeKey = key, NodeType = type, Status = AgentNodeStatuses.Pending };

    public static readonly (string Key, string Type)[] Steps = [
        (PortfolioDiagnosisNodeKeys.LoadContext, PortfolioDiagnosisNodeTypes.LoadContext),
        ("portfolioAgent", "PortfolioAgentLoop"), ("portfolioQualityGate", "PortfolioQualityGate"),
        (PortfolioDiagnosisNodeKeys.ApproveDiagnosis, HumanApprovalNodeTypes.WaitForHumanApproval),
        (PortfolioDiagnosisNodeKeys.FinalizeDiagnosis, PortfolioDiagnosisNodeTypes.FinalizeDiagnosis),
        (PortfolioDiagnosisNodeKeys.FinalizeRejectedDiagnosis, PortfolioDiagnosisNodeTypes.FinalizeRejectedDiagnosis)];

    private static JsonObject Definition()
    {
        var nodes = new JsonArray(Steps.Select(x => (JsonNode)N(x.Key, x.Type)).ToArray());
        nodes[3]!["config"] = new JsonObject { ["approvalType"] = "ApproveReject", ["prompt"] = "請審閱投組診斷後核准或拒絕。", ["subjectKey"] = AgentBlackboardKeys.PortfolioDiagnosisDraft };
        nodes[4]!["condition"] = new JsonObject { ["path"] = "humanApproval.decision", ["equals"] = "Approved" };
        nodes[5]!["condition"] = new JsonObject { ["path"] = "humanApproval.decision", ["equals"] = "Rejected" };
        return new() { ["workflowType"] = AgentWorkflowTypes.PortfolioDiagnosis, ["version"] = PortfolioDiagnosisWorkflow.Version,
            ["orchestrationMode"] = "Static", ["nodes"] = nodes,
            ["edges"] = new JsonArray(E(Steps[0].Key, Steps[1].Key), E(Steps[1].Key, Steps[2].Key), E(Steps[2].Key, Steps[3].Key), E(Steps[3].Key, Steps[4].Key), E(Steps[3].Key, Steps[5].Key)) };
    }

    private static JsonObject N(string id, string type, string? conditionPath = null, string? expected = null) => new()
    {
        ["id"] = id, ["type"] = type, ["required"] = true,
        ["condition"] = conditionPath is null ? null : new JsonObject { ["path"] = conditionPath, ["equals"] = expected }
    };
    private static JsonObject E(string from, string to) => new() { ["from"] = from, ["to"] = to };
}
