using System.Reflection;
using System.Security.Claims;
using EquityLens.Api.Common;
using EquityLens.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;

namespace EquityLens.Api.Tests.Controllers;

/// <summary>
/// 以反射檢查端點保護：避免日後新增的寫入端點忘了加 [Authorize]。
/// </summary>
public sealed class EndpointProtectionTests
{
    // 必須匿名的寫入端點（登入流程），以及只在開發環境運作、正式環境回 404 的示範資料端點。
    private static readonly HashSet<string> AnonymousWriteAllowlist = new(StringComparer.Ordinal)
    {
        "AuthController.Register",
        "AuthController.Login",
        "AuthController.GoogleLogin",
        "AuthController.Refresh",
        "DemoDataController.Seed",
        "DemoDataController.Clear",
    };

    private static readonly string[] AdminOnlyEndpoints =
    [
        "MarketPriceImportsController.ImportPrices",
        "MarketPricesController.ImportPrices",
        "SecuritiesController.RefreshAllSecurities",
        "SecuritiesController.RefreshAllPrices",
    ];

    private static readonly string[] PaidLlmEndpoints =
    [
        "ResearchController.Search",
        "ResearchController.Ask",
        "ResearchController.CreateInvestigation",
        "AgentRunsController.CreatePortfolioDiagnosis",
        "AgentRunsController.CreateCriticReview",
        "AgentRunsController.CreateDraftRevision",
        "AgentRunsController.CreateResearchQualityReview",
        "AgentRunsController.CreateEvidenceRemediation",
        "AgentRunsController.CreateEvidenceReanalysis",
        "AgentRunsController.SubmitFeedback",
        "AgentRunsController.Retry",
        "AgentWorkflowQueriesController.Create",
        "ChatController.SendMessage",
    ];

    [Fact]
    public void EveryWriteEndpoint_RequiresAuthentication_UnlessAllowlisted()
    {
        var unprotected = WriteActions()
            .Where(action => !RequiresAuthentication(action))
            .Select(Name)
            .Where(name => !AnonymousWriteAllowlist.Contains(name))
            .OrderBy(name => name)
            .ToList();

        Assert.True(unprotected.Count == 0,
            "Write endpoints without [Authorize]: " + string.Join(", ", unprotected));
    }

    [Fact]
    public void AllowlistOnlyNamesExistingAnonymousEndpoints()
    {
        var actions = WriteActions().ToDictionary(Name);
        foreach (var name in AnonymousWriteAllowlist)
        {
            Assert.True(actions.ContainsKey(name), $"allowlisted endpoint {name} no longer exists");
            Assert.False(RequiresAuthentication(actions[name]), $"{name} is protected now; remove it from the allowlist");
        }
    }

    [Theory]
    [MemberData(nameof(AdminOnly))]
    public void BatchAndImportEndpoints_AreAdminOnly(string endpoint)
    {
        var action = FindAction(endpoint);
        var roles = AuthorizeAttributes(action).Select(x => x.Roles).Where(x => x is not null).ToList();

        Assert.Contains(roles, value => value!.Split(',').Select(r => r.Trim()).Contains("Admin"));
    }

    [Theory]
    [MemberData(nameof(PaidLlm))]
    public void PaidLlmEndpoints_AreRateLimited(string endpoint)
    {
        var attribute = FindAction(endpoint).GetCustomAttribute<EnableRateLimitingAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(RateLimitPolicies.Llm, attribute!.PolicyName);
    }

    [Fact]
    public void ReadEndpoints_AreNotRateLimited()
    {
        // 前端與 eval 會輪詢執行狀態，GET 被限流會讓畫面卡住。
        var limitedReads = typeof(ApiControllerBase).Assembly.GetTypes()
            .Where(IsController)
            .SelectMany(Actions)
            .Where(action => action.GetCustomAttributes<HttpGetAttribute>().Any())
            .Where(action => action.GetCustomAttribute<EnableRateLimitingAttribute>() is not null
                || action.DeclaringType!.GetCustomAttribute<EnableRateLimitingAttribute>() is not null)
            .Select(Name)
            .ToList();

        Assert.Empty(limitedReads);
    }

    [Fact]
    public async Task LlmPartition_LimitsRegularUsersPerUser()
    {
        var options = new LlmRateLimitOptions { PermitLimit = 2, WindowSeconds = 60 };
        var alice = RateLimitPolicies.CreateLlmPartition(Principal("alice", "User"), options);
        var bob = RateLimitPolicies.CreateLlmPartition(Principal("bob", "User"), options);

        Assert.NotEqual(alice.PartitionKey, bob.PartitionKey);
        using var limiter = alice.Factory(alice.PartitionKey);
        Assert.True((await limiter.AcquireAsync()).IsAcquired);
        Assert.True((await limiter.AcquireAsync()).IsAcquired);
        Assert.False((await limiter.AcquireAsync()).IsAcquired);
    }

    [Fact]
    public async Task LlmPartition_DoesNotLimitAdmins()
    {
        var options = new LlmRateLimitOptions { PermitLimit = 1, WindowSeconds = 60 };
        var admin = RateLimitPolicies.CreateLlmPartition(Principal("root", "Admin"), options);

        using var limiter = admin.Factory(admin.PartitionKey);
        for (var i = 0; i < 20; i++)
        {
            Assert.True((await limiter.AcquireAsync()).IsAcquired);
        }
    }

    public static TheoryData<string> AdminOnly() => ToTheoryData(AdminOnlyEndpoints);

    public static TheoryData<string> PaidLlm() => ToTheoryData(PaidLlmEndpoints);

    private static TheoryData<string> ToTheoryData(IEnumerable<string> values)
    {
        var data = new TheoryData<string>();
        foreach (var value in values)
        {
            data.Add(value);
        }

        return data;
    }

    private static ClaimsPrincipal Principal(string id, string role) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, id), new Claim(ClaimTypes.Role, role)], authenticationType: "Test"));

    private static bool IsController(Type type) =>
        type is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(type);

    private static IEnumerable<MethodInfo> Actions(Type controller) =>
        controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>().Any());

    private static IEnumerable<MethodInfo> WriteActions() =>
        typeof(ApiControllerBase).Assembly.GetTypes()
            .Where(IsController)
            .SelectMany(Actions)
            .Where(action => action.GetCustomAttributes<HttpMethodAttribute>()
                .SelectMany(attribute => attribute.HttpMethods)
                .Any(method => method is "POST" or "PUT" or "PATCH" or "DELETE"));

    private static IEnumerable<AuthorizeAttribute> AuthorizeAttributes(MethodInfo action) =>
        action.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Concat(action.DeclaringType!.GetCustomAttributes<AuthorizeAttribute>(inherit: true));

    private static bool RequiresAuthentication(MethodInfo action) =>
        action.GetCustomAttribute<AllowAnonymousAttribute>() is null && AuthorizeAttributes(action).Any();

    private static string Name(MethodInfo action) => $"{action.DeclaringType!.Name}.{action.Name}";

    private static MethodInfo FindAction(string endpoint) =>
        typeof(ApiControllerBase).Assembly.GetTypes()
            .Where(IsController)
            .SelectMany(Actions)
            .Single(action => Name(action) == endpoint);
}
