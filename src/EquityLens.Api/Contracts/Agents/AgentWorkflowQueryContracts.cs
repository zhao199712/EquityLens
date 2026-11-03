namespace EquityLens.Api.Contracts.Agents;

public sealed record CreateAgentWorkflowQueryRequest(string Question, Guid? ParentAgentRunId = null);

public sealed record InvestmentResearchContextEnvelope(
    string Market,
    string Asset,
    string Depth,
    string? Horizon,
    string? Currency,
    string Language);

public sealed record InvestmentResearchRoutingContext(
    string LeadSkill,
    string LeadSkillDisplayName,
    string Objective,
    InvestmentResearchContextEnvelope ContextEnvelope,
    IReadOnlyList<string> InferredFields,
    IReadOnlyList<string> DownstreamIntents,
    IReadOnlyList<string> ClarifyingQuestions,
    string RoutingReason,
    string Confidence,
    string RoutingModel,
    string RoutingProvider,
    string PromptTemplateId,
    int PromptVersion,
    int PromptTokens,
    int CompletionTokens,
    long DurationMs,
    IReadOnlyList<PortfolioScenarioRequest>? Scenarios = null,
    Guid? ParentAgentRunId = null);

public sealed record AgentWorkflowQueryCreatedResponse(
    Guid AgentRunId,
    Guid? ResearchRunId,
    string WorkflowType,
    string Status,
    string RoutingReason,
    string RoutingModel,
    string LeadSkill,
    string LeadSkillDisplayName,
    string RoutingConfidence,
    string Objective,
    InvestmentResearchContextEnvelope ContextEnvelope,
    IReadOnlyList<string> InferredFields,
    IReadOnlyList<string> ClarifyingQuestions);

/// <summary>唯讀減碼情境；未指定持股維持股數，差額保留現金。</summary>
public sealed record PortfolioScenarioRequest(string Name, IReadOnlyDictionary<string, decimal> TargetWeights, string RemainderPolicy = "cash", bool PreserveOtherHoldings = true);
