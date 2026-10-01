using System.Net;
using System.Security.Claims;
using EquityLens.Api.Common;
using EquityLens.Api.Controllers.Filters;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Tests.Controllers;

public sealed class CostProtectionTests
{
    // ---- active agent run cap ------------------------------------------------------------------

    [Fact]
    public async Task ActiveRunCap_BlocksUserAtLimit()
    {
        var userId = Guid.NewGuid();
        await using var db = CreateDbContext();
        AddRun(db, userId, AgentRunStatuses.Running);
        AddRun(db, userId, AgentRunStatuses.Pending);
        await db.SaveChangesAsync();

        var (executing, nextCalled) = await RunFilterAsync(db, Principal(userId, "User"));

        Assert.False(nextCalled);
        var result = Assert.IsType<ObjectResult>(executing.Result);
        Assert.Equal(StatusCodes.Status429TooManyRequests, result.StatusCode);
        Assert.Equal("active_run_limit", Assert.IsType<ApiError>(result.Value).Code);
    }

    [Fact]
    public async Task ActiveRunCap_IgnoresFinishedRunsOtherUsersAndStaleRuns()
    {
        var userId = Guid.NewGuid();
        await using var db = CreateDbContext();
        AddRun(db, userId, AgentRunStatuses.Running);
        AddRun(db, userId, AgentRunStatuses.Succeeded);
        AddRun(db, userId, AgentRunStatuses.Failed);
        AddRun(db, userId, AgentRunStatuses.Running, createdAtUtc: DateTime.UtcNow.AddHours(-3)); // stuck after a crash
        AddRun(db, Guid.NewGuid(), AgentRunStatuses.Running);
        await db.SaveChangesAsync();

        var (executing, nextCalled) = await RunFilterAsync(db, Principal(userId, "User"));

        Assert.True(nextCalled);
        Assert.Null(executing.Result);
    }

    [Fact]
    public async Task ActiveRunCap_DoesNotApplyToAdmins()
    {
        var userId = Guid.NewGuid();
        await using var db = CreateDbContext();
        for (var i = 0; i < 5; i++) AddRun(db, userId, AgentRunStatuses.Running);
        await db.SaveChangesAsync();

        var (_, nextCalled) = await RunFilterAsync(db, Principal(userId, "Admin"));

        Assert.True(nextCalled);
    }

    // ---- forwarded headers -------------------------------------------------------------------

    [Theory]
    [InlineData("172.18.0.5", "198.51.100.7", "198.51.100.7")] // from Caddy on the Docker network
    [InlineData("172.18.0.5", "6.6.6.6, 198.51.100.7", "198.51.100.7")] // spoofed left-most value ignored
    [InlineData("203.0.113.9", "198.51.100.7", "203.0.113.9")] // untrusted sender: header ignored
    public async Task ForwardedHeaders_UseOnlyTheAddressAppendedByTheTrustedProxy(string peer, string forwardedFor, string expected)
    {
        var services = new ServiceCollection();
        services.AddEquityLensForwardedHeaders();
        var options = services.BuildServiceProvider().GetRequiredService<IOptions<ForwardedHeadersOptions>>();
        IPAddress? seen = null;
        var middleware = new ForwardedHeadersMiddleware(
            context => { seen = context.Connection.RemoteIpAddress; return Task.CompletedTask; },
            NullLoggerFactory.Instance,
            options);
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        httpContext.Request.Headers["X-Forwarded-For"] = forwardedFor;

        await middleware.Invoke(httpContext);

        Assert.Equal(IPAddress.Parse(expected), seen);
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static async Task<(ActionExecutingContext Executing, bool NextCalled)> RunFilterAsync(
        EquityLensDbContext db, ClaimsPrincipal user)
    {
        var filter = new ActiveAgentRunLimitFilter(db, Options.Create(new ActiveAgentRunLimitOptions
        {
            MaxActiveRunsPerUser = 2,
            ActiveWindowMinutes = 30
        }));
        var actionContext = new ActionContext(new DefaultHttpContext { User = user }, new RouteData(), new ActionDescriptor());
        var executing = new ActionExecutingContext(actionContext, [], new Dictionary<string, object?>(), controller: new object());
        var nextCalled = false;
        await filter.OnActionExecutionAsync(executing, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, [], controller: new object()));
        });
        return (executing, nextCalled);
    }

    private static void AddRun(EquityLensDbContext db, Guid userId, string status, DateTime? createdAtUtc = null) =>
        db.AgentRuns.Add(new AgentRun
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            WorkflowType = "ResearchInvestigation",
            AgentType = "Test",
            Status = status,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        });

    private static ClaimsPrincipal Principal(Guid userId, string role) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, role)], "Test"));

    private static EquityLensDbContext CreateDbContext() => new TestEquityLensDbContext(
        new DbContextOptionsBuilder<EquityLensDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class TestEquityLensDbContext(DbContextOptions<EquityLensDbContext> options) : EquityLensDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Ignore<DocumentEmbedding>();
        }
    }
}
