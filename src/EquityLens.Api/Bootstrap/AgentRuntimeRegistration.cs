using EquityLens.Api.Services.Agents.Loop;
using EquityLens.Api.Services.AdminJobs;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.BackgroundWorkers;
using EquityLens.Api.Services.Documents;

namespace EquityLens.Api.Bootstrap;

public static class AgentRuntimeRegistration
{
    public static void AddEquityLensAgents(this WebApplicationBuilder builder, bool e2eTestMode)
    {
        if (e2eTestMode) EquityLens.Api.Services.Auth.E2ETestAccess.RequireEnabled(builder.Configuration, builder.Environment);
        builder.Services.AddOptions<ResearchAgentLoopOptions>().Bind(builder.Configuration.GetSection(ResearchAgentLoopOptions.SectionName))
            .PostConfigure(x => x.DefaultEngine = "AgentLoop")
            .Validate(x => x.MaxTurns >= 2 && x.MaxRevisionTurns >= 2 && x.MaxToolCalls > 0 && x.MaxToolCallsPerTurn > 0
                && x.MaxWallClockSeconds > 0 && x.ToolTimeoutSeconds > 0 && x.ApprovalExpiryHours > 0 && x.MaxCostUsd > 0
                && x.InputUsdPerMillion > 0 && x.OutputUsdPerMillion > 0 && x.MaxOutputTokens > 0
                && x.MaxSkills >= 0 && x.DefaultEngine == "AgentLoop", "Invalid research loop limits.");
        builder.Services.AddScoped<IResearchLoopTools, ResearchLoopTools>();
        builder.Services.AddSingleton(new AgentMethodCatalog(Path.Combine(builder.Environment.ContentRootPath, "AgentSkills")));
        builder.Services.AddScoped<ResearchAgentLoop>();
        builder.Services.AddScoped<PortfolioLoopTools>();
        builder.Services.AddScoped<IAgentNodeHandler, PortfolioAgentLoopNodeHandler>();
        builder.Services.AddScoped<IAgentNodeHandler, PortfolioQualityGateNodeHandler>();
        builder.Services.AddScoped<IAgentWorkflowDefinitionProvider, ResearchAgentWorkflowDefinitionProvider>();
        builder.Services.AddScoped<IWorkflowPolicyEvaluator, ResearchAgentPolicyEvaluator>();
        builder.Services.AddScoped<IAgentNodeHandler, ResearchAgentLoopNodeHandler>();
        builder.Services.AddScoped<IAgentNodeHandler, QualityGateNodeHandler>();
        builder.Services.AddScoped<IAgentNodeHandler, FinalizeResearchAgentNodeHandler>();
        builder.Services.AddScoped<ICriticReviewAgent, LlmCriticReviewAgent>();
        if (e2eTestMode) builder.Services.AddSingleton<IAgentWorkflowCatalog, E2EAgentWorkflowCatalog>();
        else builder.Services.AddSingleton<IAgentWorkflowCatalog, AgentWorkflowCatalog>();
        builder.Services.AddScoped<IAgentWorkflowAdminService, AgentWorkflowAdminService>();
        builder.Services.AddScoped<IAgentApprovalService, AgentApprovalService>();
        builder.Services.AddScoped<IPromptManagementService, PromptManagementService>();
        builder.Services.AddScoped<IAgentPromptSnapshotService, AgentPromptSnapshotService>();
        builder.Services.AddScoped<PromptManagementBootstrapService>();
        builder.Services.AddScoped<IAgentWorkflowDefinitionProvider, CriticReviewWorkflowDefinitionProvider>();
        if (e2eTestMode)
        {
            builder.Services.AddScoped<E2EApprovalGateWorkflowDefinitionProvider>();
            builder.Services.AddScoped<IAgentWorkflowDefinitionProvider>(sp => sp.GetRequiredService<E2EApprovalGateWorkflowDefinitionProvider>());
        }
        builder.Services.AddScoped<PortfolioDiagnosisWorkflowDefinitionProvider>();
        builder.Services.AddScoped<IAgentWorkflowDefinitionProvider>(sp => sp.GetRequiredService<PortfolioDiagnosisWorkflowDefinitionProvider>());
        builder.Services.AddSingleton<IWorkflowGraphTopologyService, WorkflowGraphTopologyService>();
        builder.Services.AddScoped<IPortfolioRiskMathInputProvider, PortfolioRiskMathInputProvider>();
        builder.Services.AddScoped<IPortfolioRiskRunResolver, PortfolioRiskRunResolver>();
        builder.Services.AddSingleton<IPortfolioRiskMathExecutor, PortfolioRiskMathExecutor>();
        builder.Services.AddScoped<InvestmentResearchRouter>();
        builder.Services.AddScoped<IInvestmentResearchRouter>(sp => sp.GetRequiredService<InvestmentResearchRouter>());
        builder.Services.AddScoped<IAgentWorkflowQueryService, AgentWorkflowQueryService>();
        builder.Services.AddScoped<IAgentRunGraphValidator, AgentRunGraphValidator>();
        builder.Services.AddScoped<WorkflowExecutionCompatibility>();
        builder.Services.AddSingleton<IAgentRunStateMachine, AgentRunStateMachine>();
        builder.Services.AddSingleton<IAgentNodeStateMachine, AgentNodeStateMachine>();
        builder.Services.AddScoped<IWorkflowPolicyEvaluator, CriticReviewPolicyEvaluator>();
        builder.Services.AddScoped<IAgentNodeHandler, LoadResearchRunNodeHandler>();
        builder.Services.AddScoped<IAgentNodeHandler, FinalizeCriticReportNodeHandler>();
        builder.Services.AddScoped<IAgentNodeHandler, LoadPortfolioDiagnosisContextNodeHandler>();
        builder.Services.AddScoped<IAgentNodeHandler, FinalizePortfolioDiagnosisNodeHandler>();
        builder.Services.AddScoped<IAgentNodeHandler, ValidateResearchRequestNodeHandler>();
        builder.Services.AddScoped<IAgentNodeHandler, DetectResearchIntentNodeHandler>();
        if (e2eTestMode) builder.Services.AddScoped<IAgentNodeHandler, E2EApprovalGateNodeHandler>();
        builder.Services.AddScoped<AgentWorkflowPolicyReader>();
        builder.Services.AddScoped<IAgentNodeRunner, AgentNodeRunner>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<AgentRunLeaseHeartbeat>();
        builder.Services.AddScoped<IAgentRunExecutor, AgentRunExecutor>();
        builder.Services.AddScoped<IAgentRunService, AgentRunService>();
        builder.Services.AddScoped<IBackgroundJobExecutor, BackgroundJobExecutor>();
        if (builder.Configuration.GetValue("BackgroundJobs:Enabled", true))
            builder.Services.AddHostedService<BackgroundJobWorker>();
        builder.Services.AddHostedService<AgentRunWorker>();
        builder.Services.AddHostedService<AgentRunWakeOutboxDispatcher>();
        builder.Services.AddHostedService<RiskPythonShadowResultWorker>();
        builder.Services.AddHostedService<RiskPythonTimeoutWorker>();
        builder.Services.AddHttpClient<IEmbeddingService, OpenAiEmbeddingService>();
    }
}
