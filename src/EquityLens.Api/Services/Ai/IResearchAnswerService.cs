using EquityLens.Api.Contracts.Research;

namespace EquityLens.Api.Services.Ai;

public interface IResearchAnswerService
{
    Task<ResearchAskResponse> AskAsync(ResearchAskRequest request, CancellationToken cancellationToken = default);
}
