using EquityLens.Api.Bootstrap;
using EquityLens.Api.Contracts.Auth;
using EquityLens.Api.Controllers;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Repositories.Users;
using EquityLens.Api.Services.Auth;
using EquityLens.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EquityLens.Api.Tests.Controllers;

public sealed class AccountSecurityTests
{
    [Theory]
    [InlineData("Production", false)]
    [InlineData("Development", false)]
    [InlineData("Testing", false)]
    [InlineData("Testing", true)]
    public async Task PersistedTestAdmin_LoginRefreshAndSignedJwtRequireExplicitTestMode(string environment, bool e2e)
    {
        using var db = RefactorTestDbContext.Create();
        await using var app = BuildApp(db, environment, e2e);
        var user = new AppUser { Id = Guid.NewGuid(), Email = E2ETestAccess.AdminEmail.ToUpperInvariant(), Role = "Admin" };
        var password = Guid.NewGuid().ToString("N");
        user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, password);
        db.Users.Add(user);
        var rawRefresh = Guid.NewGuid().ToString();
        var refresh = new RefreshToken { Id = Guid.NewGuid(), UserId = user.Id, Token = RefreshTokenDigest.Compute(rawRefresh), ExpiresAtUtc = DateTime.UtcNow.AddDays(1) };
        db.RefreshTokens.Add(refresh);
        await db.SaveChangesAsync();
        var controller = CreateController(app, db);
        var login = await controller.Login(new LoginRequest(user.Email, password), default);
        var refreshed = await controller.Refresh(new RefreshRequest(rawRefresh), default);
        var token = new TokenService(app.Configuration).GenerateAccessToken(user);
        Assert.Equal(e2e, (await Authenticate(app, token)).Succeeded);
        if (e2e)
        {
            Assert.IsType<OkObjectResult>(login.Result);
            Assert.IsType<OkObjectResult>(refreshed.Result);
        }
        else
        {
            Assert.IsType<UnauthorizedObjectResult>(login.Result);
            Assert.IsType<UnauthorizedObjectResult>(refreshed.Result);
            Assert.Single(db.RefreshTokens); // No replacement token was issued.
        }
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("demote")]
    [InlineData("delete")]
    public async Task SignedJwt_IsRejectedAfterAccountRevocation(string change)
    {
        using var db = RefactorTestDbContext.Create();
        await using var app = BuildApp(db);
        var user = new AppUser { Id = Guid.NewGuid(), Email = "real-admin@example.test", Role = "Admin" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var token = new TokenService(app.Configuration).GenerateAccessToken(user);
        Assert.True((await Authenticate(app, token)).Succeeded);

        if (change == "disable") user.IsActive = false;
        if (change == "demote") user.Role = "User";
        if (change == "delete") db.Users.Remove(user);
        await db.SaveChangesAsync();
        Assert.False((await Authenticate(app, token)).Succeeded);
    }

    [Fact]
    public async Task OrdinaryUser_CanRegisterLoginRefreshAndAuthenticate()
    {
        using var db = RefactorTestDbContext.Create();
        await using var app = BuildApp(db);
        var controller = CreateController(app, db);
        var password = Guid.NewGuid().ToString("N");
        var registered = await controller.Register(new RegisterRequest("ordinary@example.test", password, "Ordinary"), default);
        var response = Assert.IsType<AuthResponse>(Assert.IsType<OkObjectResult>(registered.Result).Value);
        Assert.True((await Authenticate(app, response.AccessToken)).Succeeded);
        Assert.IsType<OkObjectResult>((await controller.Login(new LoginRequest(response.User.Email, password), default)).Result);
        Assert.IsType<OkObjectResult>((await controller.Refresh(new RefreshRequest(response.RefreshToken), default)).Result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReservedEmail_CannotRegisterOrLinkGoogleEvenInTestMode(bool e2e)
    {
        using var db = RefactorTestDbContext.Create();
        await using var app = BuildApp(db, e2e ? "Testing" : "Production", e2e);
        var email = $" {E2ETestAccess.AdminEmail.ToUpperInvariant()} ";
        var controller = CreateController(app, db, new GoogleUserPayload("subject", email, true, "Test"));
        Assert.IsType<BadRequestObjectResult>((await controller.Register(new RegisterRequest(email, Guid.NewGuid().ToString("N"), null), default)).Result);
        Assert.IsType<UnauthorizedObjectResult>((await controller.GoogleLogin(new GoogleLoginRequest("id-token"), default)).Result);
        Assert.Empty(db.Users);
        Assert.Empty(db.RefreshTokens);
    }

    [Fact]
    public async Task DisabledAccount_CannotLinkGoogleOrObtainTokens()
    {
        using var db = RefactorTestDbContext.Create();
        await using var app = BuildApp(db);
        var user = new AppUser { Id = Guid.NewGuid(), Email = "disabled@example.test", IsActive = false };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var controller = CreateController(app, db, new GoogleUserPayload("subject", user.Email, true, null));
        Assert.IsType<UnauthorizedObjectResult>((await controller.GoogleLogin(new GoogleLoginRequest("id-token"), default)).Result);
        Assert.Null((await db.Users.SingleAsync()).GoogleSubject);
        Assert.Empty(db.RefreshTokens);
    }

    [Fact]
    public async Task RefreshToken_StoredValueCannotBeUsedAsBearerCredential()
    {
        using var db = RefactorTestDbContext.Create();
        await using var app = BuildApp(db);
        var controller = CreateController(app, db);
        var registered = await controller.Register(new RegisterRequest("digest@example.test", Guid.NewGuid().ToString("N"), null), default);
        var response = Assert.IsType<AuthResponse>(Assert.IsType<OkObjectResult>(registered.Result).Value);
        var stored = (await db.RefreshTokens.SingleAsync()).Token;
        Assert.NotEqual(response.RefreshToken, stored);
        Assert.StartsWith("sha256:", stored);
        Assert.IsType<UnauthorizedObjectResult>((await controller.Refresh(new RefreshRequest(stored), default)).Result);
        Assert.IsType<OkObjectResult>((await controller.Refresh(new RefreshRequest(response.RefreshToken), default)).Result);
        Assert.IsType<UnauthorizedObjectResult>((await controller.Refresh(new RefreshRequest(response.RefreshToken), default)).Result);
        Assert.All(db.RefreshTokens, row => Assert.StartsWith("sha256:", row.Token));
    }

    [Fact]
    public async Task LegacyPlaintextRefreshToken_IsRejectedWithoutFallback()
    {
        using var db = RefactorTestDbContext.Create();
        await using var app = BuildApp(db);
        var user = new AppUser { Id = Guid.NewGuid(), Email = "legacy@example.test" };
        db.Users.Add(user);
        var raw = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
        db.RefreshTokens.Add(new RefreshToken { Id = Guid.NewGuid(), UserId = user.Id, Token = raw, ExpiresAtUtc = DateTime.UtcNow.AddDays(1) });
        await db.SaveChangesAsync();
        Assert.IsType<UnauthorizedObjectResult>((await CreateController(app, db).Refresh(new RefreshRequest(raw), default)).Result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(31)]
    public void WeakJwtKey_IsRejectedDuringRegistration(int bytes)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], ContentRootPath = Path.GetTempPath(), EnvironmentName = "Production" });
        builder.Configuration["Jwt:Key"] = new string('x', bytes);
        Assert.Throws<InvalidOperationException>(() => builder.AddEquityLensHttp());
    }

    private static WebApplication BuildApp(EquityLensDbContext db, string environment = "Production", bool e2e = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [], ContentRootPath = Path.GetTempPath(), EnvironmentName = environment
        });
        builder.Configuration["E2E_TEST_MODE"] = e2e ? "1" : "0";
        builder.Configuration["E2E_ADMIN_PASSWORD"] = e2e ? Guid.NewGuid().ToString("N") : null;
        builder.Configuration["Jwt:Key"] = "security-tests-only-signing-key-at-least-32-characters";
        builder.Configuration["Jwt:Issuer"] = "security-tests";
        builder.Configuration["Jwt:Audience"] = "security-tests";
        builder.Configuration["Jwt:ExpireMinutes"] = "60";
        builder.Configuration["Google:ClientId"] = "security-tests";
        builder.Services.AddSingleton(db);
        builder.AddEquityLensHttp();
        return builder.Build();
    }

    private static AuthController CreateController(WebApplication app, EquityLensDbContext db, GoogleUserPayload? payload = null) =>
        new(db, new UserRepository(db), new TokenService(app.Configuration), new PasswordHasher<AppUser>(),
            new GoogleValidator(payload), app.Configuration, app.Services.GetRequiredService<AccountAccessPolicy>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    private static async Task<AuthenticateResult> Authenticate(WebApplication app, string token)
    {
        using var scope = app.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Headers.Authorization = $"Bearer {token}";
        return await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
    }

    private sealed class GoogleValidator(GoogleUserPayload? payload) : IGoogleTokenValidator
    {
        public Task<GoogleUserPayload?> ValidateAsync(string idToken, CancellationToken cancellationToken = default) => Task.FromResult(payload);
    }
}
