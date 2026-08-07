using System.Text.Json.Nodes;
using EquityLens.Api.Services.Agents;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class BlackboardVersioningTests
{
    [Theory]
    [InlineData("CreateInitialCriticReviewBlackboard")]
    [InlineData("CreateInitialPortfolioDiagnosisBlackboard")]
    [InlineData("CreateInitialDraftRevisionBlackboard")]
    [InlineData("CreateInitialResearchQualityReviewBlackboard")]
    [InlineData("CreateInitialEvidenceRemediationBlackboard")]
    [InlineData("CreateInitialEvidenceReanalysisBlackboard")]
    public void CreateInitialBlackboard_IncludesVersioningFields(string methodName)
    {
        var board = CreateBoard(methodName);

        Assert.Equal(methodName == "CreateInitialResearchQualityReviewBlackboard" ? 2 : 1, board["schemaVersion"]?.GetValue<int>());
        Assert.Equal(0, board["blackboardVersion"]?.GetValue<int>());
    }

    [Fact]
    public void DetectChangedKeys_ReturnsKeysThatChanged()
    {
        var before = new JsonObject { ["question"] = "old", ["answer"] = null, ["schemaVersion"] = 1, ["blackboardVersion"] = 0 };
        var after = new JsonObject { ["question"] = "new", ["answer"] = "yes", ["schemaVersion"] = 1, ["blackboardVersion"] = 0 };

        var changed = AgentNodeJson.DetectChangedKeys(before, after);

        Assert.Contains("question", changed);
        Assert.Contains("answer", changed);
        Assert.Equal(2, changed.Length);
    }

    [Fact]
    public void DetectChangedKeys_IgnoresVersionKeys()
    {
        var before = new JsonObject { ["question"] = "same", ["schemaVersion"] = 1, ["blackboardVersion"] = 0 };
        var after = new JsonObject { ["question"] = "same", ["schemaVersion"] = 1, ["blackboardVersion"] = 1 };

        var changed = AgentNodeJson.DetectChangedKeys(before, after);

        Assert.Empty(changed);
    }

    [Fact]
    public void DetectChangedKeys_DetectsNewKeys()
    {
        var before = new JsonObject { ["question"] = "same" };
        var after = new JsonObject { ["question"] = "same", ["answer"] = "new" };

        var changed = AgentNodeJson.DetectChangedKeys(before, after);

        Assert.Contains("answer", changed);
    }

    [Fact]
    public void DetectChangedKeys_IgnoresUnchangedKeys()
    {
        var before = new JsonObject { ["question"] = "same", ["answer"] = "yes" };
        var after = new JsonObject { ["question"] = "same", ["answer"] = "yes" };

        var changed = AgentNodeJson.DetectChangedKeys(before, after);

        Assert.Empty(changed);
    }

    [Fact]
    public void IncrementBlackboardVersion_IncrementsByOne()
    {
        var board = new JsonObject { ["blackboardVersion"] = 0 };

        AgentNodeJson.IncrementBlackboardVersion(board);

        Assert.Equal(1, board["blackboardVersion"]?.GetValue<int>());
    }

    [Fact]
    public void IncrementBlackboardVersion_IncrementsFromNonZero()
    {
        var board = new JsonObject { ["blackboardVersion"] = 5 };

        AgentNodeJson.IncrementBlackboardVersion(board);

        Assert.Equal(6, board["blackboardVersion"]?.GetValue<int>());
    }

    [Fact]
    public void IncrementBlackboardVersion_DefaultsToZero_WhenMissing()
    {
        var board = new JsonObject();

        AgentNodeJson.IncrementBlackboardVersion(board);

        Assert.Equal(1, board["blackboardVersion"]?.GetValue<int>());
    }

    [Fact]
    public void DetectChangedKeys_JsonStringComparison_IsExact()
    {
        var before = new JsonObject { ["data"] = JsonNode.Parse("{\"a\":1,\"b\":2}") };
        var after = new JsonObject { ["data"] = JsonNode.Parse("{\"b\":2,\"a\":1}") };

        var changed = AgentNodeJson.DetectChangedKeys(before, after);

        Assert.Contains("data", changed);
    }

    private static JsonObject CreateBoard(string methodName)
    {
        return methodName switch
        {
            "CreateInitialCriticReviewBlackboard" => AgentBlackboardContracts.CreateInitialCriticReviewBlackboard(Guid.NewGuid()),
            "CreateInitialPortfolioDiagnosisBlackboard" => AgentBlackboardContracts.CreateInitialPortfolioDiagnosisBlackboard(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)), DateOnly.FromDateTime(DateTime.UtcNow)),
            "CreateInitialDraftRevisionBlackboard" => AgentBlackboardContracts.CreateInitialDraftRevisionBlackboard(Guid.NewGuid()),
            "CreateInitialResearchQualityReviewBlackboard" => AgentBlackboardContracts.CreateInitialResearchQualityReviewBlackboard(Guid.NewGuid()),
            "CreateInitialEvidenceRemediationBlackboard" => AgentBlackboardContracts.CreateInitialEvidenceRemediationBlackboard(Guid.NewGuid()),
            "CreateInitialEvidenceReanalysisBlackboard" => AgentBlackboardContracts.CreateInitialEvidenceReanalysisBlackboard(Guid.NewGuid()),
            _ => throw new ArgumentException($"Unknown method: {methodName}")
        };
    }
}
