using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Research;

namespace EquityLens.Api.Services.Research;

public interface IResearchPreflightService
{
    Task<Result<ResearchPreflightResult>> ValidateAskAsync(
        ResearchAskRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ResearchPreflightResult(
    Guid SecurityId,
    string Ticker,
    string Exchange,
    string SecurityName,
    int DocumentCount,
    int ChunkCount,
    int EmbeddingCount);
