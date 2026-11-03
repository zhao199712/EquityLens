using System.Security.Claims;
using EquityLens.Api.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.Auth;

/// <summary>每次 JWT 驗證重新確認帳號狀態，讓停用與角色變更立即生效。</summary>
public sealed class AccountJwtBearerEvents(EquityLensDbContext db, AccountAccessPolicy accessPolicy) : JwtBearerEvents
{
    public override async Task TokenValidated(TokenValidatedContext context)
    {
        if (!Guid.TryParse(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            context.Fail("Invalid account.");
            return;
        }

        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, context.HttpContext.RequestAborted);
        var roles = context.Principal!.FindAll(ClaimTypes.Role).Select(x => x.Value).ToArray();
        if (user is null || !accessPolicy.CanAuthenticate(user) || roles.Length != 1 || roles[0] != user.Role)
            context.Fail("Account is unavailable or its role has changed.");
    }
}
