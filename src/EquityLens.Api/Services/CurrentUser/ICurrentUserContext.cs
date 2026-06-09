using System.Security.Claims;

namespace EquityLens.Api.Services.CurrentUser;

public interface ICurrentUserContext
{
    Guid UserId { get; }
    string Email { get; }
    string DisplayName { get; }
    bool IsAuthenticated { get; }
}

public sealed class CurrentUserContext : ICurrentUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    public Guid UserId
    {
        get
        {
            var id = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return id != null ? Guid.Parse(id) : Guid.Empty;
        }
    }

    public string Email => User?.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty;

    public string DisplayName => User?.FindFirst(ClaimTypes.GivenName)?.Value ?? string.Empty;
}
