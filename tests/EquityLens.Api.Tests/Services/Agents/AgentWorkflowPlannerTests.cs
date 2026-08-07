using System.Text.Json;
using EquityLens.Api.Services.Agents;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class AgentWorkflowPlannerTests
{
    [Fact]
    public void GetExecutionOrder_LinearWorkflow_ReturnsTopologicalOrder()
    {
        var planner = new AgentWorkflowPlanner();
        var workflow = SerializeWorkflow(
            ["a", "b", "c"],
            [("a", "b"), ("b", "c")]);

        var order = planner.GetExecutionOrder(workflow);

        Assert.Equal(["a", "b", "c"], order);
    }

    [Fact]
    public void GetExecutionOrder_CriticReviewWorkflow_ReturnsExpectedOrder()
    {
        var provider = new CriticReviewWorkflowDefinitionProvider();
        var run = provider.CreateRun(Guid.NewGuid(), Guid.NewGuid());
        var planner = new AgentWorkflowPlanner();

        var order = planner.GetExecutionOrder(run.WorkflowDefinitionJson);

        Assert.Equal(
        [
            CriticReviewNodeKeys.LoadResearchRun,
            CriticReviewNodeKeys.BuildEvidencePacket,
            CriticReviewNodeKeys.CheckEvidence,
            CriticReviewNodeKeys.CritiqueAnswer,
            CriticReviewNodeKeys.FinalizeCriticReport
        ],
        order);
    }

    [Fact]
    public void GetExecutionOrder_ResearchQualityReviewWorkflow_ReturnsExpectedDynamicPrefix()
    {
        var provider = new ResearchQualityReviewWorkflowDefinitionProvider();
        var run = provider.CreateRun(Guid.NewGuid(), Guid.NewGuid());
        var planner = new AgentWorkflowPlanner();

        var order = planner.GetExecutionOrder(run.WorkflowDefinitionJson);

        Assert.Equal(
        [
            ResearchQualityReviewNodeKeys.LoadResearchRun,
            ResearchQualityReviewNodeKeys.BuildEvidencePacket,
            ResearchQualityReviewNodeKeys.CheckEvidence,
            ResearchQualityReviewNodeKeys.CritiqueAnswer,
            ResearchQualityReviewNodeKeys.FinalizeCriticReport
        ],
        order);
    }

    [Fact]
    public void GetExecutionOrder_PortfolioDiagnosisWorkflow_ReturnsExpectedOrder()
    {
        var provider = new PortfolioDiagnosisWorkflowDefinitionProvider();
        var run = provider.CreateRun(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1));
        var planner = new AgentWorkflowPlanner();

        Assert.Equal(
        [
            PortfolioDiagnosisNodeKeys.LoadContext,
            PortfolioDiagnosisNodeKeys.ResolveRiskEvidence,
            PortfolioRiskMathNodeKeys.PrepareInputs,
            PortfolioRiskMathNodeKeys.ExecuteCore,
            PortfolioDiagnosisNodeKeys.CalculateAttribution,
            PortfolioDiagnosisNodeKeys.LoadRiskProfile,
            PortfolioDiagnosisNodeKeys.PrioritizeRiskAnalyses,
            PortfolioDiagnosisNodeKeys.EvaluateQuality
        ], planner.GetExecutionOrder(run.WorkflowDefinitionJson));
    }

    [Fact]
    public void GetExecutionOrder_EvidenceRemediationWorkflow_ReturnsExpectedBoundedLoopOrder()
    {
        var run = new EvidenceRemediationWorkflowDefinitionProvider().CreateRun(Guid.NewGuid(), Guid.NewGuid());
        var order = new AgentWorkflowPlanner().GetExecutionOrder(run.WorkflowDefinitionJson);

        Assert.Equal(
        [
            EvidenceRemediationNodeKeys.LoadContext,
            EvidenceRemediationNodeKeys.ExtractClaims,
            "planEvidenceRetrieval:1", "retrieveRemediationEvidence:1", "assessClaimSupport:1", "validateEvidenceMappings:1", "routeEvidenceRemediation:1",
            "planEvidenceRetrieval:2", "retrieveRemediationEvidence:2", "assessClaimSupport:2", "validateEvidenceMappings:2", "routeEvidenceRemediation:2",
            EvidenceRemediationNodeKeys.BuildPacket,
            EvidenceRemediationNodeKeys.DraftRevision,
            EvidenceRemediationNodeKeys.Finalize
        ], order);
        var catalog = new AgentWorkflowCatalog().GetWorkflow(AgentWorkflowTypes.EvidenceRemediation);
        Assert.Equal(10, catalog.NodeTypes.Count);
        Assert.Equal(9, catalog.Edges.Count);
    }

    [Fact]
    public void Catalog_AllNodeContracts_AreCompleteAndUnique()
    {
        var catalog = new AgentWorkflowCatalog();

        Assert.Equal(catalog.Nodes.Count, catalog.Nodes.Select(x => x.NodeType).Distinct().Count());
        Assert.All(catalog.Nodes, node =>
        {
            var contract = node.Contract;
            Assert.True(contract.Version > 0);
            Assert.False(string.IsNullOrWhiteSpace(contract.InputSchema));
            Assert.False(string.IsNullOrWhiteSpace(contract.OutputSchema));
            Assert.False(string.IsNullOrWhiteSpace(contract.Stage));
            Assert.NotNull(contract.RequiredBlackboardKeys);
            Assert.NotNull(contract.ProducedBlackboardKeys);
        });
    }

    [Fact]
    public void Catalog_ResearchQualityReview_UsesExpectedSharedNodeContracts()
    {
        var workflow = new AgentWorkflowCatalog().GetWorkflow(AgentWorkflowTypes.ResearchQualityReview);

        Assert.Equal("研究品質審查", workflow.DisplayName);
        Assert.Equal(AgentTypes.Critic, workflow.AgentType);
        Assert.Equal(
        [
            ResearchQualityReviewNodeTypes.LoadResearchRun,
            ResearchQualityReviewNodeTypes.BuildEvidencePacket,
            ResearchQualityReviewNodeTypes.CheckEvidence,
            ResearchQualityReviewNodeTypes.CritiqueAnswer,
            ResearchQualityReviewNodeTypes.FinalizeCriticReport,
            ResearchQualityReviewNodeTypes.DraftRevisedAnswer,
            ResearchQualityReviewNodeTypes.FinalizeRevision
        ], workflow.NodeTypes);
        Assert.Equal(6, workflow.Edges.Count);
        Assert.Equal((ResearchQualityReviewNodeTypes.FinalizeCriticReport, ResearchQualityReviewNodeTypes.DraftRevisedAnswer), workflow.Edges[4]);
        Assert.Equal((ResearchQualityReviewNodeTypes.DraftRevisedAnswer, ResearchQualityReviewNodeTypes.FinalizeRevision), workflow.Edges[5]);
    }

    [Fact]
    public void GetExecutionOrder_EdgeReferencesUnknownNode_Throws()
    {
        var planner = new AgentWorkflowPlanner();
        var workflow = SerializeWorkflow(
            ["a"],
            [("a", "missing")]);

        var exception = Assert.Throws<InvalidOperationException>(() => planner.GetExecutionOrder(workflow));

        Assert.Equal("Workflow edge references unknown to node 'missing'.", exception.Message);
    }

    [Fact]
    public void GetExecutionOrder_Cycle_Throws()
    {
        var planner = new AgentWorkflowPlanner();
        var workflow = SerializeWorkflow(
            ["a", "b", "c"],
            [("a", "b"), ("b", "c"), ("c", "a")]);

        var exception = Assert.Throws<InvalidOperationException>(() => planner.GetExecutionOrder(workflow));

        Assert.Equal("Workflow definition contains a cycle.", exception.Message);
    }

    private static string SerializeWorkflow(IReadOnlyList<string> nodeIds, IReadOnlyList<(string From, string To)> edges) =>
        JsonSerializer.Serialize(new
        {
            workflowType = "TestWorkflow",
            version = 1,
            nodes = nodeIds.Select(id => new { id, type = id, required = true }),
            edges = edges.Select(edge => new { from = edge.From, to = edge.To })
        });
}
