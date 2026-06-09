using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Repositories.Users;

public sealed class UserRepository : IUserRepository
{
    private readonly EquityLensDbContext _dbContext;

    public UserRepository(EquityLensDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken)
    {
        return _dbContext.Users.AnyAsync(x => x.Id == id, cancellationToken);
    }

    public void Add(AppUser user)
    {
        _dbContext.Users.Add(user);
    }
}
