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
    public void Validate_DynamicRetrieveArguments_SatisfyRetrievalPlanDependency()
    {
        var run = DynamicRetrieveRun("""{"searchIntents":[{"topic":"台積電資本支出","targetClaims":["FCF impact"],"preferredSourceRoles":["Primary"],"topK":5,"freshness":"year"}]}""");
        new AgentRunGraphValidator().Validate(run, ["retrieve:1"]);
    }

    [Theory]
    [InlineData("{\"searchIntents\":[]}")]
    [InlineData("{\"searchIntents\":[{\"topic\":\"x\",\"topK\":99}]}")]
    [InlineData("not-json")]
    public void Validate_InvalidDynamicRetrieveArguments_DoNotBypassDependency(string input)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new AgentRunGraphValidator().Validate(DynamicRetrieveRun(input), ["retrieve:1"]));
        Assert.Contains("retrievalPlan", exception.Message);
    }

    [Fact]
    public void RetryPlannedArguments_AreRestoredFromDefinitionSnapshot()
    {
        var definition = """{"nodes":[{"id":"load","type":"LoadResearchRun"},{"id":"retrieve:1","type":"RetrieveRemediationEvidence","plannedArguments":{"searchIntents":[{"topic":"台積電","topK":5}]}}]}""";
        var arguments = AgentRunService.GetPlannedArguments(definition);
        var restored = JsonNode.Parse(arguments["retrieve:1"])!;
        Assert.False(arguments.ContainsKey("load")); Assert.Equal("台積電", restored["searchIntents"]![0]!["topic"]!.GetValue<string>());
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

    private static AgentRun DynamicRetrieveRun(string input) => new()
    {
        Id = Guid.NewGuid(), BlackboardJson = new JsonObject { [AgentBlackboardKeys.Ticker] = "2330", [AgentBlackboardKeys.Question] = "問題" }.ToJsonString(),
        Nodes = [new AgentRunNode { Id = Guid.NewGuid(), NodeKey = "retrieve:1", NodeType = EvidenceRemediationNodeTypes.RetrieveEvidence, Status = AgentNodeStatuses.Pending, InputJson = input }]
    };
}
