using System.Security.Claims;
using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Auth;
using EquityLens.Api.Controllers;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Repositories.Users;
using EquityLens.Api.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace EquityLens.Api.Tests.Controllers;

public sealed class AuthControllerGoogleLoginTests
{
    [Fact]
    public async Task GoogleLogin_NewUser_CreatesUserAndReturnsTokens()
    {
        using var db = CreateDb();
        var payload = new GoogleUserPayload("google-sub-1", "new@example.test", true, "New User");
        var controller = CreateController(db, new FakeGoogleTokenValidator(payload));

        var result = await controller.GoogleLogin(new GoogleLoginRequest("id-token"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<AuthResponse>(ok.Value);
        Assert.Equal("Bearer", response.TokenType);
        Assert.False(string.IsNullOrEmpty(response.AccessToken));
        Assert.False(string.IsNullOrEmpty(response.RefreshToken));

        var user = await db.Users.SingleAsync();
        Assert.Equal("new@example.test", user.Email);
        Assert.Equal("New User", user.DisplayName);
        Assert.Equal("google-sub-1", user.GoogleSubject);
        Assert.Null(user.PasswordHash);
        Assert.Equal("User", user.Role);
        Assert.True(user.IsActive);
    }

    [Fact]
    public async Task GoogleLogin_ExistingEmailUser_LinksGoogleSubject()
    {
        using var db = CreateDb();
        var existing = CreateUser("existing@example.test");
        db.Users.Add(existing);
        await db.SaveChangesAsync();

        var payload = new GoogleUserPayload("google-sub-2", "existing@example.test", true, null);
        var controller = CreateController(db, new FakeGoogleTokenValidator(payload));

        var result = await controller.GoogleLogin(new GoogleLoginRequest("id-token"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(1, await db.Users.CountAsync());

        var user = await db.Users.SingleAsync();
        Assert.Equal(existing.Id, user.Id);
        Assert.Equal("google-sub-2", user.GoogleSubject);
        Assert.Equal("hashed-password", user.PasswordHash);
    }

    [Fact]
    public async Task GoogleLogin_ReturningGoogleUser_ReturnsTokensWithoutCreatingUser()
    {
        using var db = CreateDb();
        var existing = CreateUser("returning@example.test");
        existing.GoogleSubject = "google-sub-3";
        db.Users.Add(existing);
        await db.SaveChangesAsync();

        var payload = new GoogleUserPayload("google-sub-3", "returning@example.test", true, "Returning User");
        var controller = CreateController(db, new FakeGoogleTokenValidator(payload));

        var result = await controller.GoogleLogin(new GoogleLoginRequest("id-token"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<AuthResponse>(ok.Value);
        Assert.Equal(existing.Id, response.User.Id);
        Assert.Equal(1, await db.Users.CountAsync());
    }

    [Fact]
    public async Task GoogleLogin_EmailNotVerified_ReturnsUnauthorized()
    {
        using var db = CreateDb();
        var payload = new GoogleUserPayload("google-sub-4", "unverified@example.test", false, "Unverified");
        var controller = CreateController(db, new FakeGoogleTokenValidator(payload));

        var result = await controller.GoogleLogin(new GoogleLoginRequest("id-token"), CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        var error = Assert.IsType<ApiError>(unauthorized.Value);
        Assert.Equal("auth.google_email_not_verified", error.Code);
        Assert.Equal(0, await db.Users.CountAsync());
    }

    [Fact]
    public async Task GoogleLogin_InvalidToken_ReturnsUnauthorized()
    {
        using var db = CreateDb();
        var controller = CreateController(db, new FakeGoogleTokenValidator(null));

        var result = await controller.GoogleLogin(new GoogleLoginRequest("bad-token"), CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        var error = Assert.IsType<ApiError>(unauthorized.Value);
        Assert.Equal("auth.google_invalid_token", error.Code);
        Assert.Equal(0, await db.Users.CountAsync());
    }

    [Fact]
    public async Task GoogleLogin_NotConfigured_ReturnsServiceUnavailable()
    {
        using var db = CreateDb();
        var controller = CreateController(db, new FakeGoogleTokenValidator(null), googleClientId: "");

        var result = await controller.GoogleLogin(new GoogleLoginRequest("id-token"), CancellationToken.None);

        var statusResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, statusResult.StatusCode);
        var error = Assert.IsType<ApiError>(statusResult.Value);
        Assert.Equal("auth.google_not_configured", error.Code);
    }

    private static AuthController CreateController(
        EquityLensDbContext db,
        IGoogleTokenValidator googleTokenValidator,
        string googleClientId = "test-client-id")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "test-key-at-least-32-characters-long!!",
                ["Jwt:Issuer"] = "EquityLens.Api",
                ["Jwt:Audience"] = "EquityLens.Web",
                ["Jwt:ExpireMinutes"] = "60",
                ["Jwt:RefreshExpireDays"] = "7",
                ["Google:ClientId"] = googleClientId
            })
            .Build();

        var controller = new AuthController(
            db,
            new UserRepository(db),
            new FakeTokenService(),
            new PasswordHasher<AppUser>(),
            googleTokenValidator,
            configuration)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        return controller;
    }

    private static AppUser CreateUser(string email)
    {
        var now = DateTime.UtcNow;
        return new AppUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = email.Split('@')[0],
            PasswordHash = "hashed-password",
            Role = "User",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    private static TestDb CreateDb() => new(new DbContextOptionsBuilder<EquityLensDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class TestDb(DbContextOptions<EquityLensDbContext> options) : EquityLensDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Ignore<DocumentEmbedding>();
        }
    }

    private sealed class FakeGoogleTokenValidator(GoogleUserPayload? payload) : IGoogleTokenValidator
    {
        public Task<GoogleUserPayload?> ValidateAsync(string idToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(payload);
    }

    private sealed class FakeTokenService : ITokenService
    {
        public string GenerateAccessToken(AppUser user) => "access-token";

        public string GenerateRefreshToken() => Guid.NewGuid().ToString("N");

        public ClaimsPrincipal? GetPrincipalFromExpiredToken(string token) => null;
    }
}
