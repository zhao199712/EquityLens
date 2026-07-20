using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class BlackboardSnapshotTests
{
    [Fact]
    public void AgentRunNode_HasBlackboardSnapshotJson_DefaultsToNull()
    {
        var node = new AgentRunNode();

        Assert.Null(node.BlackboardSnapshotJson);
    }

    [Fact]
    public void AgentRun_EnableBlackboardSnapshots_DefaultsToFalse()
    {
        var run = new AgentRun();

        Assert.False(run.EnableBlackboardSnapshots);
    }

    [Fact]
    public void AgentRunNode_CanSetBlackboardSnapshotJson()
    {
        var node = new AgentRunNode();
        var snapshot = "{\"question\":\"test\",\"answer\":\"yes\"}";

        node.BlackboardSnapshotJson = snapshot;

        Assert.Equal(snapshot, node.BlackboardSnapshotJson);
    }

    [Fact]
    public void AgentRun_CanEnableBlackboardSnapshots()
    {
        var run = new AgentRun { EnableBlackboardSnapshots = true };

        Assert.True(run.EnableBlackboardSnapshots);
    }

    [Fact]
    public void EvidenceRemediationWorkflowProvider_SetsEnableBlackboardSnapshots()
    {
        var provider = new EvidenceRemediationWorkflowDefinitionProvider();

        var run = provider.CreateRun(Guid.NewGuid(), Guid.NewGuid());

        Assert.True(run.EnableBlackboardSnapshots);
    }

    [Fact]
    public void EvidenceReanalysisWorkflowProvider_SetsEnableBlackboardSnapshots()
    {
        var provider = new EvidenceReanalysisWorkflowDefinitionProvider();

        var run = provider.CreateRun(Guid.NewGuid(), Guid.NewGuid());

        Assert.True(run.EnableBlackboardSnapshots);
    }

    [Fact]
    public void CriticReviewWorkflowProvider_DoesNotEnableSnapshots()
    {
        var provider = new CriticReviewWorkflowDefinitionProvider();

        var run = provider.CreateRun(Guid.NewGuid(), Guid.NewGuid());

        Assert.False(run.EnableBlackboardSnapshots);
    }

    [Fact]
    public void DraftRevisionWorkflowProvider_DoesNotEnableSnapshots()
    {
        var provider = new DraftRevisionWorkflowDefinitionProvider();

        var run = provider.CreateRun(Guid.NewGuid(), Guid.NewGuid());

        Assert.False(run.EnableBlackboardSnapshots);
    }
}
