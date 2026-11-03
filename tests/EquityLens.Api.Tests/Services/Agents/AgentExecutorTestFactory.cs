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
        IAgentWorkflowPlanner? dynamicPlanner = null,
        IDynamicPlanValidator? dynamicPlanValidator = null,
        IGraphMaterializer? graphMaterializer = null,
        IWorkflowSkillCatalog? skills = null,
        INodeCapabilityRegistry? capabilities = null,
        IAgentWorkflowAdminService? workflowAdminService = null,
        IAgentLoopController? loopController = null,
        WorkflowExecutionCompatibility? compatibility = null)
    {
        var policies = new AgentWorkflowPolicyReader(catalog, workflowAdminService);
        var catalogForPlanning = catalog ?? new AgentWorkflowCatalog();
        var registry = capabilities ?? new NodeCapabilityRegistry();
        var runner = new AgentNodeRunner(dbContext, runStateMachine, nodeStateMachine, nodeHandlers, policies);
        var dynamicPlans = new AgentDynamicPlanCoordinator(
            dbContext,
            dynamicPlanner ?? new DeterministicPlannerAdapter(),
            dynamicPlanValidator ?? new DynamicPlanValidator(registry, catalogForPlanning, topology),
            graphMaterializer ?? new GraphMaterializer(dbContext, catalogForPlanning),
            skills ?? new WorkflowSkillCatalog(),
            registry,
            loopController ?? new AgentLoopController([new ResearchQualityReviewLoopPolicy(), new PortfolioDiagnosisLoopPolicy()]),
            policies);
        return new AgentRunExecutor(dbContext, topology, runGraphValidator, runStateMachine, runner, dynamicPlans, logger, compatibility: compatibility);
    }

    private sealed class DeterministicPlannerAdapter : IAgentWorkflowPlanner
    {
        public Task<DynamicPlanProposal> PlanAsync(WorkflowPlanningContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(DeterministicDynamicWorkflowPlanner.Create(context, "Dynamic LLM planner is not registered."));
    }
}
