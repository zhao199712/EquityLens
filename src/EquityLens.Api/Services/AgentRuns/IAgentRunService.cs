using EquityLens.Api.Common;
using EquityLens.Api.Contracts.AgentRun;

namespace EquityLens.Api.Services.AgentRuns;

public interface IAgentRunService
{
    Task<Result<AgentRunCreatedResponse>> CreateCriticReviewAsync(CreateCriticReviewRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<AgentRunListItemResponse>> ListAsync(int limit, string? workflowType, string? status, CancellationToken cancellationToken);
    Task<Result<AgentRunDetailResponse>> GetDetailAsync(Guid runId, CancellationToken cancellationToken);
    Task<Result<AgentRunCreatedResponse>> RetryAsync(Guid runId, CancellationToken cancellationToken);
    Task<Result<bool>> CancelAsync(Guid runId, CancellationToken cancellationToken);
}
