using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Repositories.Users;

public interface IUserRepository
{
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);
    void Add(AppUser user);
}
