using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;

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
