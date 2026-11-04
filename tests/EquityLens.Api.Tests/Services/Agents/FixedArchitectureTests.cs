using EquityLens.Api.Bootstrap;
using EquityLens.Api.Services.Agents;
using Microsoft.AspNetCore.Builder;
namespace EquityLens.Api.Tests.Services.Agents;
public sealed class FixedArchitectureTests
{
    [Fact]
    public void ProductionHasThreeWorkflowsAndThirteenRegisteredHandlers()
    {
        var catalog = new AgentWorkflowCatalog();
        Assert.Equal(new[] { "CriticReview", "PortfolioDiagnosis", "ResearchAgent" }, catalog.Workflows.Select(w => w.WorkflowType).Order());
        Assert.Equal(13, catalog.Nodes.Count);
        var builder = WebApplication.CreateBuilder(); builder.AddEquityLensAgents(false);
        var registrations = builder.Services.Where(s => s.ServiceType == typeof(IAgentNodeHandler)).ToArray();
        Assert.Equal(13, registrations.Length); Assert.Equal(13, registrations.Select(s => s.ImplementationType).Distinct().Count());
        var actualHandlers = typeof(AgentRunExecutor).Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(IAgentNodeHandler).IsAssignableFrom(t) && t != typeof(E2EApprovalGateNodeHandler)).ToArray();
        Assert.Equal(13, actualHandlers.Length);
        Assert.Equal(actualHandlers.OrderBy(t => t.Name), registrations.Select(s => s.ImplementationType!).OrderBy(t => t.Name));
    }
}
