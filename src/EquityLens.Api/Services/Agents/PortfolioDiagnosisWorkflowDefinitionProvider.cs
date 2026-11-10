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
        (PortfolioDiagnosisNodeKeys.FinalizeDiagnosis, PortfolioDiagnosisNodeTypes.FinalizeDiagnosis)];

    private static JsonObject Definition()
    {
        var nodes = new JsonArray(Steps.Select(x => (JsonNode)N(x.Key, x.Type)).ToArray());
        return new() { ["workflowType"] = AgentWorkflowTypes.PortfolioDiagnosis, ["version"] = PortfolioDiagnosisWorkflow.Version,
            ["orchestrationMode"] = "Static", ["nodes"] = nodes,
            ["edges"] = new JsonArray(Steps.Zip(Steps.Skip(1), (from, to) => (JsonNode)E(from.Key, to.Key)).ToArray()) };
    }

    private static JsonObject N(string id, string type) => new() { ["id"] = id, ["type"] = type, ["required"] = true, ["condition"] = null };
    private static JsonObject E(string from, string to) => new() { ["from"] = from, ["to"] = to };
}
