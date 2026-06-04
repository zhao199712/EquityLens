using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Securities;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Services.Securities;

public interface ISecurityService
{
    Task<IReadOnlyList<SecurityResponse>> SearchAsync(string? query, CancellationToken cancellationToken);
    Task<IReadOnlyList<SecuritySearchResult>> SearchAvailableAsync(string? query, CancellationToken cancellationToken);
    Task<Result<SecurityResponse>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<SecurityResponse>> CreateAsync(CreateSecurityRequest request, CancellationToken cancellationToken);
    Task<Result<ResolveSecurityResponse>> ResolveAsync(ResolveSecurityRequest request, CancellationToken cancellationToken);
    Task<Result<Security>> EnsureAsync(EnsureSecurityRequest request, CancellationToken cancellationToken);
}
