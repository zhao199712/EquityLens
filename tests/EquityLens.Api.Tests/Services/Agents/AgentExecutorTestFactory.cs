using EquityLens.Api.Data;
using EquityLens.Api.Services.Agents;
using Microsoft.Extensions.Logging;

namespace EquityLens.Api.Tests.Services.Agents;

internal static class AgentExecutorTestFactory
{
    public static AgentRunExecutor Create(
        EquityLensDbContext dbContext,
        IWorkflowGraphTopologyService topology,
        IAgentRunGraphValidator runGraphValidator,
        IAgentRunStateMachine runStateMachine,
        IAgentNodeStateMachine nodeStateMachine,
        IEnumerable<IAgentNodeHandler> nodeHandlers,
        ILogger<AgentRunExecutor> logger,
        IAgentWorkflowCatalog? catalog = null,
        IAgentWorkflowAdminService? workflowAdminService = null,
        WorkflowExecutionCompatibility? compatibility = null)
    {
        var policies = new AgentWorkflowPolicyReader(catalog, workflowAdminService);
        var runner = new AgentNodeRunner(dbContext, runStateMachine, nodeStateMachine, nodeHandlers, policies);
        return new AgentRunExecutor(dbContext, topology, runGraphValidator, runStateMachine, runner, logger, compatibility: compatibility);
    }

}
