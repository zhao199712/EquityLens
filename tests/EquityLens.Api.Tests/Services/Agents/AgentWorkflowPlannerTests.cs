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
    public void GetExecutionOrder_ResearchQualityReviewWorkflow_ReturnsExpected7NodeOrder()
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
            ResearchQualityReviewNodeKeys.FinalizeCriticReport,
            ResearchQualityReviewNodeKeys.DraftRevisedAnswer,
            ResearchQualityReviewNodeKeys.FinalizeRevision
        ],
        order);
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
