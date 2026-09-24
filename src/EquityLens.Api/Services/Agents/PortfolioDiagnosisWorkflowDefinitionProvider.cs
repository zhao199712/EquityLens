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
        InvestmentResearchRoutingContext? routingContext = null) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, WorkflowType = AgentWorkflowTypes.PortfolioDiagnosis,
        AgentType = AgentTypes.Portfolio, Status = AgentRunStatuses.Pending,
        InputJson = AgentNodeJson.Serialize(new PortfolioDiagnosisInput(portfolioId, from, to, routingContext)),
        BlackboardJson = CreateInitialBlackboardJson(portfolioId, from, to, routingContext),
        WorkflowDefinitionJson = Definition().ToJsonString(AgentNodeJson.SerializerOptions),
        CreatedAtUtc = DateTime.UtcNow,
        Nodes = [
            Node(PortfolioDiagnosisNodeKeys.LoadContext, PortfolioDiagnosisNodeTypes.LoadContext),
            Node(PortfolioDiagnosisNodeKeys.ResolveRiskEvidence, PortfolioDiagnosisNodeTypes.ResolveRiskEvidence),
            Node(PortfolioRiskMathNodeKeys.PrepareInputs, PortfolioRiskMathNodeTypes.PrepareInputs),
            new AgentRunNode { Id = Guid.NewGuid(), NodeKey = PortfolioRiskMathNodeKeys.ExecuteCore, NodeType = PortfolioRiskMathNodeTypes.Execute, Status = AgentNodeStatuses.Pending, InputJson = AgentNodeJson.Serialize(new { operations = new[] { "calculate-portfolio-return", "calculate-concentration", "calculate-annualized-volatility", "calculate-max-drawdown" } }) },
            Node(PortfolioDiagnosisNodeKeys.CalculateAttribution, PortfolioDiagnosisNodeTypes.CalculateAttribution),
            Node(PortfolioDiagnosisNodeKeys.LoadRiskProfile, PortfolioDiagnosisNodeTypes.LoadRiskProfile),
            Node(PortfolioDiagnosisNodeKeys.PrioritizeRiskAnalyses, PortfolioDiagnosisNodeTypes.PrioritizeRiskAnalyses),
            Node(PortfolioDiagnosisNodeKeys.EvaluateQuality, PortfolioDiagnosisNodeTypes.EvaluateQuality)]
    };

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

    private static JsonObject Definition() => new()
    {
        ["workflowType"] = AgentWorkflowTypes.PortfolioDiagnosis,
        ["version"] = PortfolioDiagnosisWorkflow.Version,
        ["orchestrationMode"] = "DynamicStateful",
        ["loopProfile"] = new JsonObject
        {
            ["type"] = AgentWorkflowTypes.PortfolioDiagnosis,
            ["version"] = PortfolioDiagnosisWorkflow.LoopProfileVersion,
            ["maxAnalysisIterations"] = PortfolioDiagnosisWorkflow.MaxAnalysisIterations,
            ["maxDynamicNodes"] = PortfolioDiagnosisWorkflow.MaxDynamicNodes,
            ["maxMathCapabilitiesPerIteration"] = PortfolioDiagnosisWorkflow.MaxMathCapabilitiesPerIteration
        },
        ["goalStatus"] = "PendingPlanning",
        ["planningHistory"] = new JsonArray(),
        ["nodes"] = new JsonArray
        {
            N(PortfolioDiagnosisNodeKeys.LoadContext, PortfolioDiagnosisNodeTypes.LoadContext), N(PortfolioDiagnosisNodeKeys.ResolveRiskEvidence, PortfolioDiagnosisNodeTypes.ResolveRiskEvidence), N(PortfolioRiskMathNodeKeys.PrepareInputs, PortfolioRiskMathNodeTypes.PrepareInputs, AgentBlackboardKeys.CoreRiskCalculationRequired, "true"), N(PortfolioRiskMathNodeKeys.ExecuteCore, PortfolioRiskMathNodeTypes.Execute, AgentBlackboardKeys.CoreRiskCalculationRequired, "true"), N(PortfolioDiagnosisNodeKeys.CalculateAttribution, PortfolioDiagnosisNodeTypes.CalculateAttribution), N(PortfolioDiagnosisNodeKeys.LoadRiskProfile, PortfolioDiagnosisNodeTypes.LoadRiskProfile), N(PortfolioDiagnosisNodeKeys.PrioritizeRiskAnalyses, PortfolioDiagnosisNodeTypes.PrioritizeRiskAnalyses),
            N(PortfolioDiagnosisNodeKeys.EvaluateQuality, PortfolioDiagnosisNodeTypes.EvaluateQuality)
        },
        ["edges"] = new JsonArray
        {
            E(PortfolioDiagnosisNodeKeys.LoadContext, PortfolioDiagnosisNodeKeys.ResolveRiskEvidence), E(PortfolioDiagnosisNodeKeys.ResolveRiskEvidence, PortfolioRiskMathNodeKeys.PrepareInputs), E(PortfolioRiskMathNodeKeys.PrepareInputs, PortfolioRiskMathNodeKeys.ExecuteCore), E(PortfolioRiskMathNodeKeys.ExecuteCore, PortfolioDiagnosisNodeKeys.CalculateAttribution), E(PortfolioDiagnosisNodeKeys.CalculateAttribution, PortfolioDiagnosisNodeKeys.LoadRiskProfile), E(PortfolioDiagnosisNodeKeys.LoadRiskProfile, PortfolioDiagnosisNodeKeys.PrioritizeRiskAnalyses), E(PortfolioDiagnosisNodeKeys.PrioritizeRiskAnalyses, PortfolioDiagnosisNodeKeys.EvaluateQuality)
        }
    };

    private static JsonObject N(string id, string type, string? conditionPath = null, string? expected = null) => new()
    {
        ["id"] = id, ["type"] = type, ["required"] = true,
        ["condition"] = conditionPath is null ? null : new JsonObject { ["path"] = conditionPath, ["equals"] = expected }
    };
    private static JsonObject E(string from, string to) => new() { ["from"] = from, ["to"] = to };
}
