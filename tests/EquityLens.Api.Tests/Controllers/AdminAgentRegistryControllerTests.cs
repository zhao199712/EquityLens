using EquityLens.Api.Controllers;
using EquityLens.Api.Services.Agents;
using Microsoft.AspNetCore.Authorization;

namespace EquityLens.Api.Tests.Controllers;

public sealed class AdminAgentRegistryControllerTests
{
    [Fact]
    public void List_ReturnsRegisteredSkillsAndCapabilities()
    {
        var skills = new WorkflowSkillCatalog();
        var capabilities = new NodeCapabilityRegistry();
        var controller = new AdminAgentRegistryController(skills, capabilities);

        var result = controller.List();

        Assert.Equal(skills.Skills, result.Skills);
        Assert.Equal(capabilities.Capabilities, result.Capabilities);
        Assert.Contains(result.Skills, skill => skill.Id == "research-investigation");
        var conference = Assert.Single(result.Skills, skill => skill.Id == "conference-call-takeaways");
        Assert.Equal(WorkflowSkillKinds.Lead, conference.Kind);
        Assert.True(conference.Routable);
        Assert.Equal("conference-call-takeaways", conference.PromptTemplateId);
        Assert.Equal(1, conference.PromptVersion);
        Assert.Contains(AgentWorkflowTypes.ResearchInvestigation, conference.SupportedWorkflowTypes);
        Assert.Contains(result.Capabilities, capability => capability.Id == "plan-research-retrieval");
    }

    [Fact]
    public void Controller_RequiresAdminRole()
    {
        var authorize = Assert.Single(typeof(AdminAgentRegistryController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>());

        Assert.Equal("Admin", authorize.Roles);
    }
}
