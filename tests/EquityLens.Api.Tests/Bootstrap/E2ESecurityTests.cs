using EquityLens.Api.Bootstrap;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Auth;
using EquityLens.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace EquityLens.Api.Tests.Bootstrap;

public sealed class E2ESecurityTests
{
    [Theory]
    [InlineData("Production", "1", true)]
    [InlineData("Staging", "1", true)]
    [InlineData("Development", "1", true)]
    [InlineData("Testing", "1", false)]
    public async Task UnsafeConfiguration_RejectsEveryE2EEntryPoint(string environment, string enabled, bool password)
    {
        var builder = CreateBuilder(environment, enabled, password ? Guid.NewGuid().ToString("N") : null);
        Assert.Throws<InvalidOperationException>(() => E2ETestAccess.IsEnabled(builder.Configuration, builder.Environment));
        Assert.Throws<InvalidOperationException>(() => builder.AddEquityLensAgents(true));
        await using var app = builder.Build();
        Assert.Throws<InvalidOperationException>(() => app.MapEquityLensE2E());
        // No database is registered: rejection must happen before touching any service/data.
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.SeedEquityLensE2EAsync());
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Testing")]
    public async Task DisabledMode_CannotMapOrSeed(string environment)
    {
        var builder = CreateBuilder(environment, "0", null);
        Assert.False(E2ETestAccess.IsEnabled(builder.Configuration, builder.Environment));
        await using var app = builder.Build();
        Assert.Throws<InvalidOperationException>(() => app.MapEquityLensE2E());
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.SeedEquityLensE2EAsync());
    }

    [Fact]
    public async Task Seed_UsesExplicitPasswordAndRotatesItOnNextRun()
    {
        using var db = RefactorTestDbContext.Create();
        var firstPassword = Guid.NewGuid().ToString("N");
        var builder = CreateBuilder("Testing", "1", firstPassword);
        builder.Services.AddSingleton<EquityLensDbContext>(db);
        var hasher = new PasswordHasher<AppUser>();
        builder.Services.AddSingleton<IPasswordHasher<AppUser>>(hasher);
        await using var app = builder.Build();
        await app.SeedEquityLensE2EAsync();
        var user = Assert.Single(db.Users);
        Assert.Equal("Admin", user.Role);
        Assert.Equal(E2ETestAccess.AdminEmail, user.Email);
        Assert.NotEqual(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(user, user.PasswordHash!, firstPassword));

        var nextPassword = Guid.NewGuid().ToString("N");
        app.Configuration["E2E_ADMIN_PASSWORD"] = nextPassword;
        await app.SeedEquityLensE2EAsync();
        Assert.Single(db.Users);
        Assert.Equal(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(user, user.PasswordHash!, firstPassword));
        Assert.NotEqual(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(user, user.PasswordHash!, nextPassword));
    }

    [Fact]
    public async Task Seed_DoesNotPromoteExistingOrdinaryUser()
    {
        using var db = RefactorTestDbContext.Create();
        var user = new AppUser { Id = Guid.NewGuid(), Email = E2ETestAccess.AdminEmail, Role = "User" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var builder = CreateBuilder("Testing", "1", Guid.NewGuid().ToString("N"));
        builder.Services.AddSingleton<EquityLensDbContext>(db);
        builder.Services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
        await using var app = builder.Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.SeedEquityLensE2EAsync());
        Assert.Equal("User", user.Role);
        Assert.Null(user.PasswordHash);
    }

    private static WebApplicationBuilder CreateBuilder(string environment, string enabled, string? password)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [], ContentRootPath = Path.GetTempPath(), EnvironmentName = environment
        });
        builder.Configuration["E2E_TEST_MODE"] = enabled;
        builder.Configuration["E2E_ADMIN_PASSWORD"] = password;
        return builder;
    }
}
