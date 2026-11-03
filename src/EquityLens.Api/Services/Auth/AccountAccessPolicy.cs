using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Services.Auth;

/// <summary>統一登入、刷新與既有 JWT 的帳號存取條件。</summary>
public sealed class AccountAccessPolicy(IConfiguration configuration, IHostEnvironment environment)
{
    private readonly bool _e2eEnabled = E2ETestAccess.IsEnabled(configuration, environment);

    public bool CanAuthenticate(AppUser user) =>
        user.IsActive && (_e2eEnabled || !E2ETestAccess.IsReservedEmail(user.Email));
}
