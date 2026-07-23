namespace EquityLens.Api.Contracts.Agents;

public sealed record CreateAgentWorkflowQueryRequest(string Question);

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
    long DurationMs);

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
