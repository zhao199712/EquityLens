using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using System.Text.Json.Nodes;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class AgentRunGraphValidatorTests
{
    [Fact]
    public void Validate_MatchingNodes_DoesNotThrow()
    {
        var validator = new AgentRunGraphValidator();
        var run = CreateRun(["a", "b"]);

        validator.Validate(run, ["a", "b"]);
    }

    [Fact]
    public void Validate_MissingPersistedNode_Throws()
    {
        var validator = new AgentRunGraphValidator();
        var run = CreateRun(["a"]);

        var exception = Assert.Throws<InvalidOperationException>(() => validator.Validate(run, ["a", "b"]));

        Assert.Equal("Agent run is missing node 'b'.", exception.Message);
    }

    [Fact]
    public void Validate_DuplicatePersistedNodeKey_Throws()
    {
        var validator = new AgentRunGraphValidator();
        var run = CreateRun(["a", "a"]);

        var exception = Assert.Throws<InvalidOperationException>(() => validator.Validate(run, ["a"]));

        Assert.Equal("Agent run contains duplicate node key 'a'.", exception.Message);
    }

    [Fact]
    public void Validate_ExtraPersistedNode_Throws()
    {
        var validator = new AgentRunGraphValidator();
        var run = CreateRun(["a", "extra"]);

        var exception = Assert.Throws<InvalidOperationException>(() => validator.Validate(run, ["a"]));

        Assert.Equal("Agent run contains node 'extra' not present in workflow definition.", exception.Message);
    }

    [Fact]
    public void NodeMetadata_AllCriticReviewNodeTypes_HaveMetadata()
    {
        var allMetadata = AgentNodeMetadata.GetAll();
        var nodeTypes = new[]
        {
            CriticReviewNodeTypes.LoadResearchRun,
            CriticReviewNodeTypes.BuildEvidencePacket,
            CriticReviewNodeTypes.CheckEvidence,
            CriticReviewNodeTypes.CritiqueAnswer,
            CriticReviewNodeTypes.FinalizeCriticReport,
            DraftRevisionNodeTypes.LoadCriticReviewRun,
            DraftRevisionNodeTypes.DraftRevisedAnswer,
            DraftRevisionNodeTypes.FinalizeRevision,
        };

        Assert.All(nodeTypes, nodeType => Assert.Contains(allMetadata.Keys, k => k == nodeType));
    }

    [Fact]
    public void NodeMetadata_CriticReviewWorkflow_AllNodesHaveRequiredAndProducedKeys()
    {
        var provider = new CriticReviewWorkflowDefinitionProvider();
        var run = provider.CreateRun(Guid.NewGuid(), Guid.NewGuid());
        var planner = new AgentWorkflowPlanner();
        var executionOrder = planner.GetExecutionOrder(run.WorkflowDefinitionJson);
        var allMetadata = AgentNodeMetadata.GetAll();

        foreach (var nodeKey in executionOrder)
        {
            var nodeType = run.Nodes.First(n => n.NodeKey == nodeKey).NodeType;
            var metadata = allMetadata.Values.FirstOrDefault(m => m.NodeType == nodeType);
            Assert.NotNull(metadata);
            Assert.NotEmpty(metadata.RequiredBlackboardKeys);
            Assert.NotEmpty(metadata.ProducedBlackboardKeys);
        }
    }

    [Fact]
    public void NodeMetadata_ResearchQualityReviewWorkflow_AllNodesHaveMetadata()
    {
        var provider = new ResearchQualityReviewWorkflowDefinitionProvider();
        var run = provider.CreateRun(Guid.NewGuid(), Guid.NewGuid());
        var planner = new AgentWorkflowPlanner();
        var executionOrder = planner.GetExecutionOrder(run.WorkflowDefinitionJson);
        var allMetadata = AgentNodeMetadata.GetAll();

        foreach (var nodeKey in executionOrder)
        {
            var nodeType = run.Nodes.First(n => n.NodeKey == nodeKey).NodeType;
            var metadata = allMetadata.Values.FirstOrDefault(m => m.NodeType == nodeType);
            Assert.NotNull(metadata);
        }
    }

    [Fact]
    public void NodeMetadataValidator_CriticReviewWorkflow_ValidBlackboard_DoesNotThrow()
    {
        var provider = new CriticReviewWorkflowDefinitionProvider();
        var researchRunId = Guid.NewGuid();
        var run = provider.CreateRun(Guid.NewGuid(), researchRunId);
        var planner = new AgentWorkflowPlanner();
        var executionOrder = planner.GetExecutionOrder(run.WorkflowDefinitionJson);

        NodeMetadataValidator.ValidateBlackboardDependencies(
            executionOrder,
            run.BlackboardJson,
            AgentNodeMetadata.GetAll());
    }

    [Fact]
    public void NodeMetadataValidator_ResearchQualityReviewWorkflow_ValidBlackboard_DoesNotThrow()
    {
        var provider = new ResearchQualityReviewWorkflowDefinitionProvider();
        var researchRunId = Guid.NewGuid();
        var run = provider.CreateRun(Guid.NewGuid(), researchRunId);
        var planner = new AgentWorkflowPlanner();
        var executionOrder = planner.GetExecutionOrder(run.WorkflowDefinitionJson);

        NodeMetadataValidator.ValidateBlackboardDependencies(
            executionOrder,
            run.BlackboardJson,
            AgentNodeMetadata.GetAll());
    }

    [Fact]
    public void NodeMetadataValidator_MissingRequiredKey_Throws()
    {
        var metadata = new Dictionary<string, NodeMetadata>(StringComparer.Ordinal)
        {
            ["nodeA"] = new("nodeA", "Test", ["keyA"], ["keyB"]),
            ["nodeB"] = new("nodeB", "Test", ["keyMissing"], ["keyC"]),
        };
        var blackboard = new JsonObject
        {
            ["keyA"] = "value"
        };
        var executionOrder = new List<string> { "nodeA", "nodeB" };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            NodeMetadataValidator.ValidateBlackboardDependencies(
                executionOrder,
                blackboard.ToJsonString(),
                metadata));

        Assert.Contains("nodeB", exception.Message);
        Assert.Contains("keyMissing", exception.Message);
    }

    [Fact]
    public void NodeMetadataValidator_ProducedKeySatisfiesDependency_DoesNotThrow()
    {
        var metadata = new Dictionary<string, NodeMetadata>(StringComparer.Ordinal)
        {
            ["nodeA"] = new("nodeA", "Test", ["keyA"], ["keyB"]),
            ["nodeB"] = new("nodeB", "Test", ["keyB"], ["keyC"]),
        };
        var blackboard = new JsonObject
        {
            ["keyA"] = "value"
        };
        var executionOrder = new List<string> { "nodeA", "nodeB" };

        NodeMetadataValidator.ValidateBlackboardDependencies(
            executionOrder,
            blackboard.ToJsonString(),
            metadata);
    }

    private static AgentRun CreateRun(IReadOnlyList<string> nodeKeys) => new()
    {
        Id = Guid.NewGuid(),
        Nodes = nodeKeys.Select(nodeKey => new AgentRunNode
        {
            Id = Guid.NewGuid(),
            NodeKey = nodeKey,
            NodeType = nodeKey,
            Status = AgentNodeStatuses.Pending
        }).ToList()
    };
}
