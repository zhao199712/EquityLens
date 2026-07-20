using EquityLens.Api.Contracts.Agents;
using EquityLens.Api.Contracts.Research;

namespace EquityLens.Api.Services.Agents;

public interface IAgentRunService
{
    Task<(AgentRunSummaryResponse AgentRun, Guid ResearchRunId)> CreateResearchInvestigationAsync(Guid userId, ResearchAskRequest request, CancellationToken cancellationToken = default);
    Task<AgentRunSummaryResponse> CreateCriticReviewAsync(
        Guid userId,
        Guid researchRunId,
        CancellationToken cancellationToken = default);

    Task<AgentRunSummaryResponse> CreateDraftRevisionAsync(
        Guid userId,
        Guid criticReviewRunId,
        CancellationToken cancellationToken = default);

    Task<AgentRunSummaryResponse> CreateResearchQualityReviewAsync(
        Guid userId,
        Guid researchRunId,
        CancellationToken cancellationToken = default);

    Task<AgentRunSummaryResponse> CreateEvidenceRemediationAsync(
        Guid userId,
        Guid criticReviewRunId,
        CancellationToken cancellationToken = default);

    Task<AgentRunSummaryResponse> CreateEvidenceReanalysisAsync(
        Guid userId,
        Guid evidenceRemediationRunId,
        CancellationToken cancellationToken = default);

    Task<AgentRunSummaryResponse> CreatePortfolioDiagnosisAsync(
        Guid userId,
        Guid portfolioId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentRunSummaryResponse>> ListAsync(
        Guid? userId,
        int limit = 50,
        string? workflowType = null,
        string? status = null,
        Guid? researchRunId = null,
        CancellationToken cancellationToken = default);

    Task<AgentRunDetailResponse?> GetByIdAsync(
        Guid id,
        Guid? userId,
        CancellationToken cancellationToken = default);

    Task<AgentRunSummaryResponse?> RetryAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<AgentRunSummaryResponse?> CancelAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default);
}
