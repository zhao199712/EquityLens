using EquityLens.Api.Contracts.Securities;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Repositories.Securities;

public interface ISecurityRepository
{
    Task<IReadOnlyList<SecurityResponse>> SearchAsync(string? query, CancellationToken cancellationToken);
    Task<SecurityResponse?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Security?> GetEntityAsync(Guid id, CancellationToken cancellationToken);
    Task<Security?> GetEntityByTickerExchangeAsync(string ticker, string exchange, CancellationToken cancellationToken);
    Task<bool> ActiveExistsAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> TickerExchangeExistsAsync(string ticker, string exchange, CancellationToken cancellationToken);
    void Add(Security security);
}
