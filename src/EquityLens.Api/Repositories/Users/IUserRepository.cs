using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Repositories.Users;

public interface IUserRepository
{
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);
    Task<AppUser?> GetByEmailAsync(string email, CancellationToken cancellationToken);
    Task<AppUser?> GetByGoogleSubjectAsync(string subject, CancellationToken cancellationToken);
    void Add(AppUser user);
}
