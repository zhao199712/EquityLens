using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Securities;

namespace EquityLens.Api.Services.Securities;

public interface ISecurityService
{
    Task<IReadOnlyList<SecurityResponse>> SearchAsync(string? query, CancellationToken cancellationToken);
    Task<Result<SecurityResponse>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<SecurityResponse>> CreateAsync(CreateSecurityRequest request, CancellationToken cancellationToken);
}
