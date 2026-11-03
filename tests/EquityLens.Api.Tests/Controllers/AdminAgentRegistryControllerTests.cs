using Microsoft.AspNetCore.Mvc;
using EquityLens.Api.Controllers;
namespace EquityLens.Api.Tests.Controllers;
public sealed class AdminRegistryBoundaryTests
{
    [Fact]
    public void NoCapabilityRegistryEndpointRemains()
    {
        var routes = typeof(AgentRunsController).Assembly.GetTypes().SelectMany(t => t.GetCustomAttributes(typeof(RouteAttribute), false).Cast<RouteAttribute>()).Select(r => r.Template);
        Assert.DoesNotContain("api/admin/agent-registry", routes);
    }
}
