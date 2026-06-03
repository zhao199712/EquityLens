using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Repositories.Users;

namespace EquityLens.Api.Services.DemoUser;

public interface IDemoUserContext
{
    Guid UserId { get; }
    Task EnsureUserAsync(CancellationToken cancellationToken);
}

public sealed class DemoUserContext : IDemoUserContext
{
    private readonly EquityLensDbContext _dbContext;
    private readonly IUserRepository _userRepository;

    public DemoUserContext(EquityLensDbContext dbContext, IUserRepository userRepository)
    {
        _dbContext = dbContext;
        _userRepository = userRepository;
    }

    public Guid UserId { get; } = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public async Task EnsureUserAsync(CancellationToken cancellationToken)
    {
        if (await _userRepository.ExistsAsync(UserId, cancellationToken))
        {
            return;
        }

        var now = DateTime.UtcNow;
        _userRepository.Add(new AppUser
        {
            Id = UserId,
            Email = "demo@equitylens.local",
            DisplayName = "Demo User",
            PasswordHash = "demo-user-no-login",
            Role = "User",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
