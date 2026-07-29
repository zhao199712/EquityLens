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
            Node(PortfolioRiskMathNodeKeys.PrepareInputs, PortfolioRiskMathNodeTypes.PrepareInputs),
            new AgentRunNode { Id = Guid.NewGuid(), NodeKey = PortfolioRiskMathNodeKeys.ExecuteCore, NodeType = PortfolioRiskMathNodeTypes.Execute, TemplateNodeKey = "portfolio-risk-core", Status = AgentNodeStatuses.Pending, InputJson = AgentNodeJson.Serialize(new { operations = new[] { "calculate-portfolio-return", "calculate-concentration", "calculate-annualized-volatility", "calculate-max-drawdown", "calculate-historical-var", "calculate-expected-shortfall", "calculate-portfolio-volatility", "calculate-volatility-risk-contribution" } }) },
            Node(PortfolioDiagnosisNodeKeys.CalculateAttribution, PortfolioDiagnosisNodeTypes.CalculateAttribution),
            Node(PortfolioDiagnosisNodeKeys.LoadRiskProfile, PortfolioDiagnosisNodeTypes.LoadRiskProfile),
            Node(PortfolioDiagnosisNodeKeys.PrioritizeRiskAnalyses, PortfolioDiagnosisNodeTypes.PrioritizeRiskAnalyses),
            Node(PortfolioDiagnosisNodeKeys.BuildEvidencePacket, PortfolioDiagnosisNodeTypes.BuildEvidencePacket),
            Node(PortfolioDiagnosisNodeKeys.DraftDiagnosis, PortfolioDiagnosisNodeTypes.DraftDiagnosis),
            Node(PortfolioDiagnosisNodeKeys.ApproveDiagnosis, HumanApprovalNodeTypes.WaitForHumanApproval),
            Node(PortfolioDiagnosisNodeKeys.FinalizeDiagnosis, PortfolioDiagnosisNodeTypes.FinalizeDiagnosis),
            Node(PortfolioDiagnosisNodeKeys.FinalizeRejectedDiagnosis, PortfolioDiagnosisNodeTypes.FinalizeRejectedDiagnosis)]
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
        ["nodes"] = new JsonArray
        {
            N(PortfolioDiagnosisNodeKeys.LoadContext, PortfolioDiagnosisNodeTypes.LoadContext), N(PortfolioRiskMathNodeKeys.PrepareInputs, PortfolioRiskMathNodeTypes.PrepareInputs), N(PortfolioRiskMathNodeKeys.ExecuteCore, PortfolioRiskMathNodeTypes.Execute), N(PortfolioDiagnosisNodeKeys.CalculateAttribution, PortfolioDiagnosisNodeTypes.CalculateAttribution), N(PortfolioDiagnosisNodeKeys.LoadRiskProfile, PortfolioDiagnosisNodeTypes.LoadRiskProfile), N(PortfolioDiagnosisNodeKeys.PrioritizeRiskAnalyses, PortfolioDiagnosisNodeTypes.PrioritizeRiskAnalyses), N(PortfolioDiagnosisNodeKeys.BuildEvidencePacket, PortfolioDiagnosisNodeTypes.BuildEvidencePacket), N(PortfolioDiagnosisNodeKeys.DraftDiagnosis, PortfolioDiagnosisNodeTypes.DraftDiagnosis),
            N(PortfolioDiagnosisNodeKeys.ApproveDiagnosis, HumanApprovalNodeTypes.WaitForHumanApproval, new JsonObject { ["approvalType"] = "ApproveReject", ["prompt"] = "請審核 AI 投組診斷草稿，批准後才會發布最終報告。", ["subjectKey"] = AgentBlackboardKeys.PortfolioDiagnosisDraft }),
            NConditional(PortfolioDiagnosisNodeKeys.FinalizeDiagnosis, PortfolioDiagnosisNodeTypes.FinalizeDiagnosis, $"{AgentBlackboardKeys.HumanApproval}.{HumanApprovalFields.Decision}", HumanApprovalDecisions.Approved),
            NConditional(PortfolioDiagnosisNodeKeys.FinalizeRejectedDiagnosis, PortfolioDiagnosisNodeTypes.FinalizeRejectedDiagnosis, $"{AgentBlackboardKeys.HumanApproval}.{HumanApprovalFields.Decision}", HumanApprovalDecisions.Rejected)
        },
        ["edges"] = new JsonArray
        {
            E(PortfolioDiagnosisNodeKeys.LoadContext, PortfolioRiskMathNodeKeys.PrepareInputs), E(PortfolioRiskMathNodeKeys.PrepareInputs, PortfolioRiskMathNodeKeys.ExecuteCore), E(PortfolioRiskMathNodeKeys.ExecuteCore, PortfolioDiagnosisNodeKeys.CalculateAttribution), E(PortfolioDiagnosisNodeKeys.CalculateAttribution, PortfolioDiagnosisNodeKeys.LoadRiskProfile), E(PortfolioDiagnosisNodeKeys.LoadRiskProfile, PortfolioDiagnosisNodeKeys.PrioritizeRiskAnalyses), E(PortfolioDiagnosisNodeKeys.PrioritizeRiskAnalyses, PortfolioDiagnosisNodeKeys.BuildEvidencePacket), E(PortfolioDiagnosisNodeKeys.BuildEvidencePacket, PortfolioDiagnosisNodeKeys.DraftDiagnosis), E(PortfolioDiagnosisNodeKeys.DraftDiagnosis, PortfolioDiagnosisNodeKeys.ApproveDiagnosis), E(PortfolioDiagnosisNodeKeys.ApproveDiagnosis, PortfolioDiagnosisNodeKeys.FinalizeDiagnosis), E(PortfolioDiagnosisNodeKeys.ApproveDiagnosis, PortfolioDiagnosisNodeKeys.FinalizeRejectedDiagnosis)
        }
    };

    private static JsonObject N(string id, string type) => new() { ["id"] = id, ["type"] = type, ["required"] = true };
    private static JsonObject N(string id, string type, JsonObject config) => new() { ["id"] = id, ["type"] = type, ["required"] = true, ["config"] = config };
    private static JsonObject NConditional(string id, string type, string conditionPath, string conditionEquals) => new() { ["id"] = id, ["type"] = type, ["required"] = true, ["condition"] = new JsonObject { ["path"] = conditionPath, ["equals"] = conditionEquals } };
    private static JsonObject E(string from, string to) => new() { ["from"] = from, ["to"] = to };
}
