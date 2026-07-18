using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Agents;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.Research;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class AgentRunServiceTests
{
    [Fact]
    public void CriticReviewPolicyEvaluator_EvidenceFinding_RoutesToResearchRetrieval()
    {
        var evaluator = new CriticReviewPolicyEvaluator();
        var criticReview = new JsonObject
        {
            [CriticReviewFields.Findings] = new JsonArray
            {
                AgentBlackboardContracts.CreateFinding("High", "WeakCitation", "引用不足。", "補強引用。")
            }
        };

        var decision = evaluator.Evaluate(new WorkflowPolicyContext(
            AgentWorkflowTypes.CriticReview,
            CriticReviewNodeKeys.FinalizeCriticReport,
            new JsonObject(),
            criticReview));

        Assert.True(decision.ShouldStop);
        Assert.False(decision.ShouldContinue);
        Assert.True(decision.RequiresRevision);
        Assert.True(decision.RequiresMoreEvidence);
        Assert.Equal("ResearchRetrieval", decision.RouteBackTo);
        Assert.Equal("CollectMoreEvidenceThenReviseAnswer", decision.RecommendedNextAction);
    }

    [Fact]
    public void CriticReviewPolicyEvaluator_NonEvidenceFinding_RoutesToAnswerGeneration()
    {
        var evaluator = new CriticReviewPolicyEvaluator();
        var criticReview = new JsonObject
        {
            [CriticReviewFields.Findings] = new JsonArray
            {
                AgentBlackboardContracts.CreateFinding("Medium", "UnsupportedClaim", "回答有未支撐推論。", "重寫回答。")
            }
        };

        var decision = evaluator.Evaluate(new WorkflowPolicyContext(
            AgentWorkflowTypes.CriticReview,
            CriticReviewNodeKeys.FinalizeCriticReport,
            new JsonObject(),
            criticReview));

        Assert.True(decision.ShouldStop);
        Assert.False(decision.ShouldContinue);
        Assert.True(decision.RequiresRevision);
        Assert.False(decision.RequiresMoreEvidence);
        Assert.Equal("AnswerGeneration", decision.RouteBackTo);
        Assert.Equal("ReviseAnswer", decision.RecommendedNextAction);
    }

    [Fact]
    public void ResearchQualityReviewPolicyEvaluator_EvidenceFinding_SetsRouteBackToNull()
    {
        var evaluator = new ResearchQualityReviewPolicyEvaluator();
        var criticReview = new JsonObject
        {
            [CriticReviewFields.Findings] = new JsonArray
            {
                AgentBlackboardContracts.CreateFinding("High", "WeakCitation", "引用不足。", "補強引用。")
            }
        };

        var decision = evaluator.Evaluate(new WorkflowPolicyContext(
            AgentWorkflowTypes.ResearchQualityReview,
            ResearchQualityReviewNodeKeys.FinalizeCriticReport,
            new JsonObject(),
            criticReview));

        Assert.True(decision.ShouldStop);
        Assert.True(decision.RequiresRevision);
        Assert.True(decision.RequiresMoreEvidence);
        Assert.Null(decision.RouteBackTo);
        Assert.Equal("ReviseAnswer", decision.RecommendedNextAction);
    }

    [Fact]
    public void ResearchQualityReviewPolicyEvaluator_NoFindings_AcceptsAnswer()
    {
        var evaluator = new ResearchQualityReviewPolicyEvaluator();
        var criticReview = new JsonObject
        {
            [CriticReviewFields.Findings] = new JsonArray()
        };

        var decision = evaluator.Evaluate(new WorkflowPolicyContext(
            AgentWorkflowTypes.ResearchQualityReview,
            ResearchQualityReviewNodeKeys.FinalizeCriticReport,
            new JsonObject(),
            criticReview));

        Assert.True(decision.ShouldStop);
        Assert.False(decision.RequiresRevision);
        Assert.Equal("AcceptAnswer", decision.RecommendedNextAction);
    }

    [Fact]
    public void ResearchQualityReviewPolicyEvaluator_NonEvidenceFinding_SetsRouteBackToNull()
    {
        var evaluator = new ResearchQualityReviewPolicyEvaluator();
        var criticReview = new JsonObject
        {
            [CriticReviewFields.Findings] = new JsonArray
            {
                AgentBlackboardContracts.CreateFinding("Medium", "UnsupportedClaim", "回答有未支撐推論。", "重寫回答。")
            }
        };

        var decision = evaluator.Evaluate(new WorkflowPolicyContext(
            AgentWorkflowTypes.ResearchQualityReview,
            ResearchQualityReviewNodeKeys.FinalizeCriticReport,
            new JsonObject(),
            criticReview));

        Assert.True(decision.ShouldStop);
        Assert.True(decision.RequiresRevision);
        Assert.False(decision.RequiresMoreEvidence);
        Assert.Null(decision.RouteBackTo);
        Assert.Equal("ReviseAnswer", decision.RecommendedNextAction);
    }

    [Fact]
    public async Task CreateCriticReviewAsync_ValidResearchRun_CompletesWorkflowAndPersistsTrace()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var researchRunId = Guid.NewGuid();
        var service = CreateService(
            db,
            new FakeResearchRunTraceService(BuildResearchRunDetail(researchRunId)),
            new DeterministicCriticReviewAgent());

        var summary = await service.CreateCriticReviewAsync(userId, researchRunId, CancellationToken.None);

        Assert.Equal(AgentRunStatuses.Succeeded, summary.Status);
        var detail = await service.GetByIdAsync(summary.Id, userId, CancellationToken.None);
        Assert.NotNull(detail);
        Assert.Equal(5, detail.Nodes.Count);
        Assert.Contains(detail.Nodes, n => n.NodeKey == CriticReviewNodeKeys.CritiqueAnswer && n.Status == AgentNodeStatuses.Succeeded);
        AssertCriticReviewWorkflowDefinition(detail.WorkflowDefinitionJson);
        Assert.Contains(detail.Events, e => e.EventType == AgentEventTypes.RunStarted);
        Assert.Contains(detail.Events, e => e.EventType == AgentEventTypes.RunSucceeded);
        Assert.Contains(detail.Events, e => e.EventType == AgentEventTypes.NodeCompleted);
        Assert.Equal(2, detail.ToolCalls.Count);
        var getResearchRunToolCall = detail.ToolCalls.Single(t => t.ToolName == "getResearchRun");
        Assert.Equal(AgentToolCallStatuses.Succeeded, getResearchRunToolCall.Status);
        var criticToolCall = detail.ToolCalls.Single(t => t.ToolName == "criticReviewLLM");
        Assert.Equal(AgentToolCallStatuses.Succeeded, criticToolCall.Status);
        Assert.NotNull(criticToolCall.ArgumentsJson);
        Assert.NotNull(criticToolCall.ResultJson);
        Assert.Contains("Medium", criticToolCall.ResultPreview);
        Assert.NotNull(detail.OutputJson);

        using var blackboard = JsonDocument.Parse(detail.BlackboardJson);
        Assert.Equal("2330", blackboard.RootElement.GetProperty(AgentBlackboardKeys.Ticker).GetString());
        Assert.Equal("answer [1]", blackboard.RootElement.GetProperty(AgentBlackboardKeys.Answer).GetString());
        AssertCriticReviewBlackboardContract(detail);

        using var output = JsonDocument.Parse(detail.OutputJson);
        Assert.Equal("Medium", output.RootElement.GetProperty(CriticReviewFields.OverallSeverity).GetString());
        Assert.True(output.RootElement.GetProperty(CriticReviewFields.RequiresRevision).GetBoolean());
        Assert.True(output.RootElement.GetProperty(CriticReviewFields.RequiresMoreEvidence).GetBoolean());
        Assert.Equal("ResearchRetrieval", output.RootElement.GetProperty(CriticReviewFields.RouteBackTo).GetString());
        Assert.Equal("CollectMoreEvidenceThenReviseAnswer", output.RootElement.GetProperty(CriticReviewFields.RecommendedNextAction).GetString());
    }

    [Fact]
    public async Task CreateCriticReviewAsync_WithQueue_EnqueuesRunAndReturnsPending()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var researchRunId = Guid.NewGuid();
        var queue = new RecordingAgentRunQueue();
        var service = CreateService(
            db,
            new FakeResearchRunTraceService(BuildResearchRunDetail(researchRunId)),
            new DeterministicCriticReviewAgent(),
            agentRunQueue: queue);

        var summary = await service.CreateCriticReviewAsync(userId, researchRunId, CancellationToken.None);

        Assert.Equal(AgentRunStatuses.Pending, summary.Status);
        var queued = Assert.Single(queue.Messages);
        Assert.Equal(summary.Id, queued.RunId);
        Assert.Equal(userId, queued.UserId);
        Assert.Equal(AgentWorkflowTypes.CriticReview, queued.WorkflowType);

        var detail = await service.GetByIdAsync(summary.Id, userId, CancellationToken.None);
        Assert.NotNull(detail);
        Assert.Contains(detail.Events, e => e.EventType == AgentEventTypes.RunCreated);
        Assert.DoesNotContain(detail.Events, e => e.EventType == AgentEventTypes.RunStarted);
    }

    [Fact]
    public async Task CreateCriticReviewAsync_ValidResearchRun_UsesStateMachinesForLifecycleTransitions()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var researchRunId = Guid.NewGuid();
        var runStateMachine = new RecordingRunStateMachine();
        var nodeStateMachine = new RecordingNodeStateMachine();
        var service = CreateService(
            db,
            new FakeResearchRunTraceService(BuildResearchRunDetail(researchRunId)),
            new DeterministicCriticReviewAgent(),
            runStateMachine,
            nodeStateMachine);

        var summary = await service.CreateCriticReviewAsync(userId, researchRunId, CancellationToken.None);

        Assert.Equal(AgentRunStatuses.Succeeded, summary.Status);
        Assert.Equal(
            [
                $"{AgentRunStatuses.Pending}->{AgentRunStatuses.Running}",
                $"{AgentRunStatuses.Running}->{AgentRunStatuses.Succeeded}"
            ],
            runStateMachine.Transitions.Select(x => $"{x.From}->{x.To}").ToArray());

        var expectedNodeTransitions = new[]
        {
            CriticReviewNodeKeys.LoadResearchRun,
            CriticReviewNodeKeys.BuildEvidencePacket,
            CriticReviewNodeKeys.CheckEvidence,
            CriticReviewNodeKeys.CritiqueAnswer,
            CriticReviewNodeKeys.FinalizeCriticReport
        }.SelectMany(nodeKey => new[]
        {
            $"{nodeKey}:{AgentNodeStatuses.Pending}->{AgentNodeStatuses.Ready}",
            $"{nodeKey}:{AgentNodeStatuses.Ready}->{AgentNodeStatuses.Queued}",
            $"{nodeKey}:{AgentNodeStatuses.Queued}->{AgentNodeStatuses.Running}",
            $"{nodeKey}:{AgentNodeStatuses.Running}->{AgentNodeStatuses.Succeeded}"
        }).ToArray();
        Assert.Equal(expectedNodeTransitions, nodeStateMachine.Transitions.Select(x => $"{x.NodeKey}:{x.From}->{x.To}").ToArray());
    }

    [Fact]
    public async Task CreateCriticReviewAsync_StrongEvidence_RecommendsAcceptAnswer()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var researchRunId = Guid.NewGuid();
        var service = CreateService(
            db,
            new FakeResearchRunTraceService(BuildResearchRunDetail(researchRunId, candidateCount: 3)),
            new DeterministicCriticReviewAgent());

        var summary = await service.CreateCriticReviewAsync(userId, researchRunId, CancellationToken.None);

        Assert.Equal(AgentRunStatuses.Succeeded, summary.Status);
        var detail = await service.GetByIdAsync(summary.Id, userId, CancellationToken.None);
        Assert.NotNull(detail);
        Assert.NotNull(detail.OutputJson);
        using var output = JsonDocument.Parse(detail.OutputJson);
        Assert.Equal("None", output.RootElement.GetProperty(CriticReviewFields.OverallSeverity).GetString());
        Assert.False(output.RootElement.GetProperty(CriticReviewFields.RequiresRevision).GetBoolean());
        Assert.False(output.RootElement.GetProperty(CriticReviewFields.RequiresMoreEvidence).GetBoolean());
        Assert.Equal(JsonValueKind.Null, output.RootElement.GetProperty(CriticReviewFields.RouteBackTo).ValueKind);
        Assert.Equal("AcceptAnswer", output.RootElement.GetProperty(CriticReviewFields.RecommendedNextAction).GetString());
    }

    [Fact]
    public async Task CreateCriticReviewAsync_CriticReturnsNoFindings_PreservesEvidenceFindingsForPolicy()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var researchRunId = Guid.NewGuid();
        var service = CreateService(
            db,
            new FakeResearchRunTraceService(BuildResearchRunDetail(researchRunId)),
            new NoFindingCriticReviewAgent());

        var summary = await service.CreateCriticReviewAsync(userId, researchRunId, CancellationToken.None);

        Assert.Equal(AgentRunStatuses.Succeeded, summary.Status);
        var detail = await service.GetByIdAsync(summary.Id, userId, CancellationToken.None);
        Assert.NotNull(detail);
        Assert.NotNull(detail.OutputJson);
        using var output = JsonDocument.Parse(detail.OutputJson);
        Assert.Equal("Medium", output.RootElement.GetProperty(CriticReviewFields.OverallSeverity).GetString());
        Assert.True(output.RootElement.GetProperty(CriticReviewFields.RequiresRevision).GetBoolean());
        Assert.True(output.RootElement.GetProperty(CriticReviewFields.RequiresMoreEvidence).GetBoolean());
        Assert.Equal("ResearchRetrieval", output.RootElement.GetProperty(CriticReviewFields.RouteBackTo).GetString());
        Assert.Equal("CollectMoreEvidenceThenReviseAnswer", output.RootElement.GetProperty(CriticReviewFields.RecommendedNextAction).GetString());
        Assert.NotEmpty(output.RootElement.GetProperty(CriticReviewFields.Findings).EnumerateArray());
    }

    [Fact]
    public async Task CreateCriticReviewAsync_MissingResearchRun_MarksRunFailed()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var service = CreateService(
            db,
            new FakeResearchRunTraceService(null),
            new DeterministicCriticReviewAgent());

        var summary = await service.CreateCriticReviewAsync(userId, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(AgentRunStatuses.Failed, summary.Status);
        Assert.Equal("Research run not found.", summary.ErrorMessage);
        var detail = await service.GetByIdAsync(summary.Id, userId, CancellationToken.None);
        Assert.NotNull(detail);
        Assert.Contains(detail.Events, e => e.EventType == AgentEventTypes.RunFailed);
        Assert.Contains(detail.ToolCalls, t => t.ToolName == "getResearchRun" && t.Status == AgentToolCallStatuses.Failed);
    }

    [Fact]
    public async Task RetryAsync_CriticReviewAgentFailure_ReplaysWorkflowAndSucceeds()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var researchRunId = Guid.NewGuid();
        var criticAgent = new FlakyCriticReviewAgent();
        var service = CreateService(
            db,
            new FakeResearchRunTraceService(BuildResearchRunDetail(researchRunId, candidateCount: 3)),
            criticAgent);

        var failed = await service.CreateCriticReviewAsync(userId, researchRunId, CancellationToken.None);

        Assert.Equal(AgentRunStatuses.Failed, failed.Status);
        Assert.Equal("critic unavailable", failed.ErrorMessage);
        var failedDetail = await service.GetByIdAsync(failed.Id, userId, CancellationToken.None);
        Assert.NotNull(failedDetail);
        Assert.Contains(failedDetail.Nodes, n => n.NodeKey == CriticReviewNodeKeys.CritiqueAnswer && n.Status == AgentNodeStatuses.Failed);
        Assert.Contains(failedDetail.Events, e => e.EventType == AgentEventTypes.NodeFailed);
        Assert.Contains(failedDetail.ToolCalls, t =>
            t.ToolName == "criticReviewLLM"
            && t.Status == AgentToolCallStatuses.Failed
            && t.ErrorMessage == "critic unavailable");
        Assert.Null(failedDetail.OutputJson);

        var retried = await service.RetryAsync(failed.Id, userId, CancellationToken.None);

        Assert.NotNull(retried);
        Assert.Equal(AgentRunStatuses.Succeeded, retried.Status);
        var retriedDetail = await service.GetByIdAsync(failed.Id, userId, CancellationToken.None);
        Assert.NotNull(retriedDetail);
        Assert.Contains(retriedDetail.Nodes, n => n.NodeKey == CriticReviewNodeKeys.CritiqueAnswer && n.Status == AgentNodeStatuses.Succeeded);
        Assert.Contains(retriedDetail.Events, e => e.EventType == AgentEventTypes.RunFailed);
        Assert.Contains(retriedDetail.Events, e => e.EventType == AgentEventTypes.RunSucceeded);
        Assert.NotNull(retriedDetail.OutputJson);
        using var output = JsonDocument.Parse(retriedDetail.OutputJson);
        Assert.Equal("AcceptAnswer", output.RootElement.GetProperty(CriticReviewFields.RecommendedNextAction).GetString());
        Assert.Equal(2, criticAgent.CallCount);
    }

    [Fact]
    public async Task CreateCriticReviewAsync_PersistsExpectedWorkflowDefinition()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var researchRunId = Guid.NewGuid();
        var service = CreateService(
            db,
            new FakeResearchRunTraceService(BuildResearchRunDetail(researchRunId, candidateCount: 3)),
            new DeterministicCriticReviewAgent());

        var summary = await service.CreateCriticReviewAsync(userId, researchRunId, CancellationToken.None);

        var detail = await service.GetByIdAsync(summary.Id, userId, CancellationToken.None);
        Assert.NotNull(detail);
        AssertCriticReviewWorkflowDefinition(detail.WorkflowDefinitionJson);
    }

    [Fact]
    public async Task RetryAsync_DoesNotChangeWorkflowDefinition()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var researchRunId = Guid.NewGuid();
        var criticAgent = new FlakyCriticReviewAgent();
        var service = CreateService(
            db,
            new FakeResearchRunTraceService(BuildResearchRunDetail(researchRunId, candidateCount: 3)),
            criticAgent);

        var failed = await service.CreateCriticReviewAsync(userId, researchRunId, CancellationToken.None);
        var failedDetail = await service.GetByIdAsync(failed.Id, userId, CancellationToken.None);
        Assert.NotNull(failedDetail);
        var originalDefinition = failedDetail.WorkflowDefinitionJson;

        var retried = await service.RetryAsync(failed.Id, userId, CancellationToken.None);

        Assert.NotNull(retried);
        Assert.Equal(AgentRunStatuses.Succeeded, retried.Status);
        var retriedDetail = await service.GetByIdAsync(failed.Id, userId, CancellationToken.None);
        Assert.NotNull(retriedDetail);
        Assert.Equal(originalDefinition, retriedDetail.WorkflowDefinitionJson);
        AssertCriticReviewWorkflowDefinition(retriedDetail.WorkflowDefinitionJson);
    }

    [Fact]
    public async Task RetryAndCancelAsync_DifferentUser_ReturnNullAndDoNotMutateRun()
    {
        await using var db = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var service = CreateService(
            db,
            new FakeResearchRunTraceService(BuildResearchRunDetail(Guid.NewGuid(), candidateCount: 3)),
            new DeterministicCriticReviewAgent());
        var summary = await service.CreateCriticReviewAsync(ownerId, Guid.NewGuid(), CancellationToken.None);

        var retryResponse = await service.RetryAsync(summary.Id, otherUserId, CancellationToken.None);
        var cancelResponse = await service.CancelAsync(summary.Id, otherUserId, CancellationToken.None);

        Assert.Null(retryResponse);
        Assert.Null(cancelResponse);
        var detail = await service.GetByIdAsync(summary.Id, ownerId, CancellationToken.None);
        Assert.NotNull(detail);
        Assert.Equal(AgentRunStatuses.Succeeded, detail.Run.Status);
        Assert.DoesNotContain(detail.Events, e => e.EventType == AgentEventTypes.RunCancelled);
    }

    [Fact]
    public async Task CreateDraftRevisionAsync_CompletedCriticReview_CreatesRevisionWorkflow()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var researchRunId = Guid.NewGuid();
        var service = CreateService(
            db,
            new FakeResearchRunTraceService(BuildResearchRunDetail(researchRunId)),
            new DeterministicCriticReviewAgent());
        var criticReview = await service.CreateCriticReviewAsync(userId, researchRunId, CancellationToken.None);

        var draftRevision = await service.CreateDraftRevisionAsync(userId, criticReview.Id, CancellationToken.None);

        Assert.Equal(AgentRunStatuses.Succeeded, draftRevision.Status);
        Assert.Equal(AgentWorkflowTypes.DraftRevision, draftRevision.WorkflowType);
        Assert.Equal(AgentTypes.Draft, draftRevision.AgentType);
        var detail = await service.GetByIdAsync(draftRevision.Id, userId, CancellationToken.None);
        Assert.NotNull(detail);
        Assert.Equal(3, detail.Nodes.Count);
        Assert.Contains(detail.Nodes, n => n.NodeKey == DraftRevisionNodeKeys.LoadCriticReviewRun && n.Status == AgentNodeStatuses.Succeeded);
        Assert.Contains(detail.Nodes, n => n.NodeKey == DraftRevisionNodeKeys.DraftRevisedAnswer && n.Status == AgentNodeStatuses.Succeeded);
        Assert.Contains(detail.Nodes, n => n.NodeKey == DraftRevisionNodeKeys.FinalizeRevision && n.Status == AgentNodeStatuses.Succeeded);
        var toolCall = Assert.Single(detail.ToolCalls, t => t.ToolName == "draftRevisionLLM");
        Assert.Equal(AgentToolCallStatuses.Succeeded, toolCall.Status);
        Assert.NotNull(toolCall.ArgumentsJson);
        Assert.NotNull(toolCall.ResultJson);
        Assert.NotNull(detail.OutputJson);
        using var output = JsonDocument.Parse(detail.OutputJson);
        Assert.True(output.RootElement.GetProperty(DraftRevisionFields.RevisionRequired).GetBoolean());
        Assert.Equal("建議補強引用支撐後再重寫回答，並避免超出來源證據的推論。", output.RootElement.GetProperty(DraftRevisionFields.RevisedAnswer).GetString());
        Assert.Equal("建議補強引用支撐後再重寫回答，並避免超出來源證據的推論。", output.RootElement.GetProperty(DraftRevisionFields.AppliedRecommendation).GetString());
        using var blackboard = JsonDocument.Parse(detail.BlackboardJson);
        Assert.Equal(criticReview.Id, blackboard.RootElement.GetProperty(AgentBlackboardKeys.CriticReviewRunId).GetGuid());
        Assert.Equal("answer [1]", blackboard.RootElement.GetProperty(AgentBlackboardKeys.Answer).GetString());
        Assert.Equal("建議補強引用支撐後再重寫回答，並避免超出來源證據的推論。", blackboard.RootElement.GetProperty(AgentBlackboardKeys.RevisedAnswer).GetString());
    }

    [Fact]
    public async Task CreateDraftRevisionAsync_RevisionWithoutSuggestedAnswer_UsesFallbackAppliedRecommendation()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var researchRunId = Guid.NewGuid();
        var service = CreateService(
            db,
            new FakeResearchRunTraceService(BuildResearchRunDetail(researchRunId)),
            new NoFindingCriticReviewAgent());
        var criticReview = await service.CreateCriticReviewAsync(userId, researchRunId, CancellationToken.None);

        var draftRevision = await service.CreateDraftRevisionAsync(userId, criticReview.Id, CancellationToken.None);

        Assert.Equal(AgentRunStatuses.Succeeded, draftRevision.Status);
        var detail = await service.GetByIdAsync(draftRevision.Id, userId, CancellationToken.None);
        Assert.NotNull(detail);
        Assert.NotNull(detail.OutputJson);
        using var output = JsonDocument.Parse(detail.OutputJson);
        Assert.True(output.RootElement.GetProperty(DraftRevisionFields.RevisionRequired).GetBoolean());
        Assert.Equal("請補強引用支撐，並避免超出來源證據的推論。", output.RootElement.GetProperty(DraftRevisionFields.AppliedRecommendation).GetString());
        Assert.Contains("修訂提醒：請補強引用支撐", output.RootElement.GetProperty(DraftRevisionFields.RevisedAnswer).GetString());
    }

    [Fact]
    public async Task CreateDraftRevisionAsync_MissingCriticReviewRun_MarksRunFailed()
    {
        await using var db = CreateDbContext();
        var service = CreateService(
            db,
            new FakeResearchRunTraceService(BuildResearchRunDetail(Guid.NewGuid())),
            new DeterministicCriticReviewAgent());

        var summary = await service.CreateDraftRevisionAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(AgentRunStatuses.Failed, summary.Status);
        Assert.Equal("Critic review run not found.", summary.ErrorMessage);
        var detail = await service.GetByIdAsync(summary.Id, null, CancellationToken.None);
        Assert.NotNull(detail);
        Assert.Contains(detail.Nodes, n => n.NodeKey == DraftRevisionNodeKeys.LoadCriticReviewRun && n.Status == AgentNodeStatuses.Failed);
        Assert.Null(detail.OutputJson);
    }

    [Fact]
    public async Task RetryAsync_DraftRevisionHandlerFailure_ReplaysWorkflowAndSucceeds()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var researchRunId = Guid.NewGuid();
        var sourceService = CreateService(
            db,
            new FakeResearchRunTraceService(BuildResearchRunDetail(researchRunId)),
            new DeterministicCriticReviewAgent());
        var criticReview = await sourceService.CreateCriticReviewAsync(userId, researchRunId, CancellationToken.None);
        var failingHandlers = CreateAllHandlers(
                new FakeResearchRunTraceService(BuildResearchRunDetail(researchRunId)),
                new DeterministicCriticReviewAgent())
            .Where(handler => handler.NodeType != DraftRevisionNodeTypes.DraftRevisedAnswer)
            .ToArray();
        var failingService = CreateServiceWithHandlers(
            db,
            [new CriticReviewWorkflowDefinitionProvider(), new DraftRevisionWorkflowDefinitionProvider()],
            failingHandlers);

        var failed = await failingService.CreateDraftRevisionAsync(userId, criticReview.Id, CancellationToken.None);

        Assert.Equal(AgentRunStatuses.Failed, failed.Status);
        Assert.Equal($"Unsupported node type '{DraftRevisionNodeTypes.DraftRevisedAnswer}'.", failed.ErrorMessage);
        var failedDetail = await failingService.GetByIdAsync(failed.Id, userId, CancellationToken.None);
        Assert.NotNull(failedDetail);
        var originalDefinition = failedDetail.WorkflowDefinitionJson;
        Assert.Contains(failedDetail.Nodes, n => n.NodeKey == DraftRevisionNodeKeys.DraftRevisedAnswer && n.Status == AgentNodeStatuses.Failed);
        Assert.Null(failedDetail.OutputJson);

        var retryService = CreateService(
            db,
            new FakeResearchRunTraceService(BuildResearchRunDetail(researchRunId)),
            new DeterministicCriticReviewAgent());
        var retried = await retryService.RetryAsync(failed.Id, userId, CancellationToken.None);

        Assert.NotNull(retried);
        Assert.Equal(AgentRunStatuses.Succeeded, retried.Status);
        var retriedDetail = await retryService.GetByIdAsync(failed.Id, userId, CancellationToken.None);
        Assert.NotNull(retriedDetail);
        Assert.Equal(originalDefinition, retriedDetail.WorkflowDefinitionJson);
        Assert.Contains(retriedDetail.Nodes, n => n.NodeKey == DraftRevisionNodeKeys.DraftRevisedAnswer && n.Status == AgentNodeStatuses.Succeeded);
        Assert.Contains(retriedDetail.Events, e => e.EventType == AgentEventTypes.RunFailed);
        Assert.Contains(retriedDetail.Events, e => e.EventType == AgentEventTypes.RunSucceeded);
        Assert.NotNull(retriedDetail.OutputJson);
        using var blackboard = JsonDocument.Parse(retriedDetail.BlackboardJson);
        Assert.Equal(criticReview.Id, blackboard.RootElement.GetProperty(AgentBlackboardKeys.CriticReviewRunId).GetGuid());
    }

    [Fact]
    public async Task CreateDraftRevisionAsync_DifferentUserCriticReviewRun_MarksRunFailed()
    {
        await using var db = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var researchRunId = Guid.NewGuid();
        var service = CreateService(
            db,
            new FakeResearchRunTraceService(BuildResearchRunDetail(researchRunId)),
            new DeterministicCriticReviewAgent());
        var criticReview = await service.CreateCriticReviewAsync(ownerId, researchRunId, CancellationToken.None);

        var summary = await service.CreateDraftRevisionAsync(otherUserId, criticReview.Id, CancellationToken.None);

        Assert.Equal(AgentRunStatuses.Failed, summary.Status);
        Assert.Equal("Critic review run not found.", summary.ErrorMessage);
        var otherUserDetail = await service.GetByIdAsync(summary.Id, otherUserId, CancellationToken.None);
        Assert.NotNull(otherUserDetail);
        Assert.Contains(otherUserDetail.Nodes, n => n.NodeKey == DraftRevisionNodeKeys.LoadCriticReviewRun && n.Status == AgentNodeStatuses.Failed);
        Assert.Null(otherUserDetail.OutputJson);
        var ownerSourceDetail = await service.GetByIdAsync(criticReview.Id, ownerId, CancellationToken.None);
        Assert.NotNull(ownerSourceDetail);
        Assert.Equal(AgentRunStatuses.Succeeded, ownerSourceDetail.Run.Status);
    }

    [Fact]
    public async Task CreateResearchQualityReviewAsync_ValidResearchRun_Completes7NodeWorkflow()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var researchRunId = Guid.NewGuid();
        var service = CreateService(
            db,
            new FakeResearchRunTraceService(BuildResearchRunDetail(researchRunId)),
            new DeterministicCriticReviewAgent());

        var summary = await service.CreateResearchQualityReviewAsync(userId, researchRunId, CancellationToken.None);

        Assert.Equal(AgentRunStatuses.Succeeded, summary.Status);
        Assert.Equal(AgentWorkflowTypes.ResearchQualityReview, summary.WorkflowType);
        var detail = await service.GetByIdAsync(summary.Id, userId, CancellationToken.None);
        Assert.NotNull(detail);
        Assert.Equal(7, detail.Nodes.Count);
        Assert.Contains(detail.Nodes, n => n.NodeKey == ResearchQualityReviewNodeKeys.LoadResearchRun && n.Status == AgentNodeStatuses.Succeeded);
        Assert.Contains(detail.Nodes, n => n.NodeKey == ResearchQualityReviewNodeKeys.BuildEvidencePacket && n.Status == AgentNodeStatuses.Succeeded);
        Assert.Contains(detail.Nodes, n => n.NodeKey == ResearchQualityReviewNodeKeys.CheckEvidence && n.Status == AgentNodeStatuses.Succeeded);
        Assert.Contains(detail.Nodes, n => n.NodeKey == ResearchQualityReviewNodeKeys.CritiqueAnswer && n.Status == AgentNodeStatuses.Succeeded);
        Assert.Contains(detail.Nodes, n => n.NodeKey == ResearchQualityReviewNodeKeys.FinalizeCriticReport && n.Status == AgentNodeStatuses.Succeeded);
        Assert.Contains(detail.Nodes, n => n.NodeKey == ResearchQualityReviewNodeKeys.DraftRevisedAnswer && n.Status == AgentNodeStatuses.Succeeded);
        Assert.Contains(detail.Nodes, n => n.NodeKey == ResearchQualityReviewNodeKeys.FinalizeRevision && n.Status == AgentNodeStatuses.Succeeded);
        Assert.NotNull(detail.OutputJson);
        Assert.Contains(detail.Events, e => e.EventType == AgentEventTypes.RunStarted);
        Assert.Contains(detail.Events, e => e.EventType == AgentEventTypes.RunSucceeded);
    }

    [Fact]
    public async Task CreateResearchQualityReviewAsync_DisabledWorkflow_IsRejectedUntilReenabled()
    {
        await using var db = CreateDbContext();
        var researchRunId = Guid.NewGuid();
        var adminService = new AgentWorkflowAdminService(db, new AgentWorkflowCatalog());
        await adminService.UpdateWorkflowAsync(
            AgentWorkflowTypes.ResearchQualityReview,
            new UpdateAgentWorkflowSettingRequest(false, null, null));
        var service = CreateService(
            db,
            new FakeResearchRunTraceService(BuildResearchRunDetail(researchRunId)),
            new DeterministicCriticReviewAgent(),
            workflowAdminService: adminService);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateResearchQualityReviewAsync(Guid.NewGuid(), researchRunId, CancellationToken.None));
        Assert.Equal("Workflow 'ResearchQualityReview' is disabled.", exception.Message);

        await adminService.UpdateWorkflowAsync(
            AgentWorkflowTypes.ResearchQualityReview,
            new UpdateAgentWorkflowSettingRequest(true, null, null));
        var summary = await service.CreateResearchQualityReviewAsync(Guid.NewGuid(), researchRunId, CancellationToken.None);

        Assert.Equal(AgentRunStatuses.Succeeded, summary.Status);
    }

    [Fact]
    public async Task CreateResearchQualityReviewAsync_CompletedWorkflow_BlackboardContainsRevisionOutput()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var researchRunId = Guid.NewGuid();
        var service = CreateService(
            db,
            new FakeResearchRunTraceService(BuildResearchRunDetail(researchRunId)),
            new DeterministicCriticReviewAgent());

        var summary = await service.CreateResearchQualityReviewAsync(userId, researchRunId, CancellationToken.None);

        var detail = await service.GetByIdAsync(summary.Id, userId, CancellationToken.None);
        Assert.NotNull(detail);
        using var blackboard = JsonDocument.Parse(detail.BlackboardJson);
        Assert.Equal("2330", blackboard.RootElement.GetProperty(AgentBlackboardKeys.Ticker).GetString());
        Assert.Equal("answer [1]", blackboard.RootElement.GetProperty(AgentBlackboardKeys.Answer).GetString());
        Assert.NotNull(detail.OutputJson);
        using var output = JsonDocument.Parse(detail.OutputJson);
        Assert.True(output.RootElement.GetProperty(DraftRevisionFields.RevisionRequired).GetBoolean());
        Assert.NotNull(blackboard.RootElement.GetProperty(AgentBlackboardKeys.CriticReview));
    }

    [Fact]
    public async Task CreateResearchQualityReviewAsync_PersistsExpectedWorkflowDefinition()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var researchRunId = Guid.NewGuid();
        var service = CreateService(
            db,
            new FakeResearchRunTraceService(BuildResearchRunDetail(researchRunId, candidateCount: 3)),
            new DeterministicCriticReviewAgent());

        var summary = await service.CreateResearchQualityReviewAsync(userId, researchRunId, CancellationToken.None);

        var detail = await service.GetByIdAsync(summary.Id, userId, CancellationToken.None);
        Assert.NotNull(detail);
        AssertResearchQualityReviewWorkflowDefinition(detail.WorkflowDefinitionJson);
    }

    [Fact]
    public async Task CreateResearchQualityReviewAsync_MissingResearchRun_MarksRunFailed()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var service = CreateService(
            db,
            new FakeResearchRunTraceService(null),
            new DeterministicCriticReviewAgent());

        var summary = await service.CreateResearchQualityReviewAsync(userId, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(AgentRunStatuses.Failed, summary.Status);
        Assert.Equal("Research run not found.", summary.ErrorMessage);
    }

    [Fact]
    public async Task RetryAsync_ResearchQualityReviewFailure_ReplaysWorkflowAndSucceeds()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var researchRunId = Guid.NewGuid();
        var criticAgent = new FlakyCriticReviewAgent();
        var service = CreateService(
            db,
            new FakeResearchRunTraceService(BuildResearchRunDetail(researchRunId, candidateCount: 3)),
            criticAgent);

        var failed = await service.CreateResearchQualityReviewAsync(userId, researchRunId, CancellationToken.None);

        Assert.Equal(AgentRunStatuses.Failed, failed.Status);
        Assert.Equal("critic unavailable", failed.ErrorMessage);
        var failedDetail = await service.GetByIdAsync(failed.Id, userId, CancellationToken.None);
        Assert.NotNull(failedDetail);
        Assert.Contains(failedDetail.Nodes, n => n.NodeKey == ResearchQualityReviewNodeKeys.CritiqueAnswer && n.Status == AgentNodeStatuses.Failed);
        Assert.Null(failedDetail.OutputJson);

        var retried = await service.RetryAsync(failed.Id, userId, CancellationToken.None);

        Assert.NotNull(retried);
        Assert.Equal(AgentRunStatuses.Succeeded, retried.Status);
        var retriedDetail = await service.GetByIdAsync(failed.Id, userId, CancellationToken.None);
        Assert.NotNull(retriedDetail);
        Assert.Equal(7, retriedDetail.Nodes.Count);
        Assert.Contains(retriedDetail.Nodes, n => n.NodeKey == ResearchQualityReviewNodeKeys.CritiqueAnswer && n.Status == AgentNodeStatuses.Succeeded);
        Assert.Contains(retriedDetail.Nodes, n => n.NodeKey == ResearchQualityReviewNodeKeys.FinalizeRevision && n.Status == AgentNodeStatuses.Succeeded);
        Assert.NotNull(retriedDetail.OutputJson);
        Assert.Equal(2, criticAgent.CallCount);
    }

    [Fact]
    public async Task CreateResearchQualityReviewAsync_StrongEvidence_FinalOutputAcceptsAnswer()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var researchRunId = Guid.NewGuid();
        var service = CreateService(
            db,
            new FakeResearchRunTraceService(BuildResearchRunDetail(researchRunId, candidateCount: 3)),
            new DeterministicCriticReviewAgent());

        var summary = await service.CreateResearchQualityReviewAsync(userId, researchRunId, CancellationToken.None);

        Assert.Equal(AgentRunStatuses.Succeeded, summary.Status);
        var detail = await service.GetByIdAsync(summary.Id, userId, CancellationToken.None);
        Assert.NotNull(detail);
        var finalizeNode = detail.Nodes.Single(n => n.NodeKey == ResearchQualityReviewNodeKeys.FinalizeCriticReport);
        Assert.NotNull(finalizeNode.OutputJson);
        using var finalizeOutput = JsonDocument.Parse(finalizeNode.OutputJson);
        Assert.Equal("None", finalizeOutput.RootElement.GetProperty(CriticReviewFields.OverallSeverity).GetString());
        Assert.False(finalizeOutput.RootElement.GetProperty(CriticReviewFields.RequiresRevision).GetBoolean());
    }

    [Fact]
    public async Task CreateEvidenceRemediationAsync_Creates9NodeRunWithPolicySnapshot()
    {
        await using var db = CreateDbContext(); var queue = new RecordingAgentRunQueue(); var state = new AgentRunStateMachine(); var nodeState = new AgentNodeStateMachine();
        var service = new AgentRunService(db, [new EvidenceRemediationWorkflowDefinitionProvider()], state, nodeState, queue);

        var summary = await service.CreateEvidenceRemediationAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(AgentWorkflowTypes.EvidenceRemediation, summary.WorkflowType); Assert.Equal(AgentRunStatuses.Pending, summary.Status); Assert.Single(queue.Messages);
        var detail = await service.GetByIdAsync(summary.Id, null, CancellationToken.None); Assert.NotNull(detail); Assert.Equal(15, detail.Nodes.Count);
        using var definition = JsonDocument.Parse(detail.WorkflowDefinitionJson); Assert.All(definition.RootElement.GetProperty("nodes").EnumerateArray(), node => Assert.True(node.TryGetProperty("executionPolicy", out _)));
    }

    [Fact]
    public async Task CreateEvidenceReanalysisAsync_CreatesSevenNodeStatefulRunWithPolicySnapshot()
    {
        await using var db = CreateDbContext(); var queue = new RecordingAgentRunQueue(); var state = new AgentRunStateMachine(); var nodeState = new AgentNodeStateMachine(); var userId = Guid.NewGuid();
        var source = new EvidenceRemediationWorkflowDefinitionProvider().CreateRun(userId, Guid.NewGuid()); source.Status = AgentRunStatuses.Succeeded; var sourceBoard = AgentNodeJson.ParseBlackboard(source.BlackboardJson); var packet = new RemediatedEvidencePacket("Supported", [new("claim-1", "claim", "Supported", [1], [])], [new(1, "LocalDocument", "年報", "AnnualReport", null, "evidence", .9)], [], true, ["關鍵結論改變"]); sourceBoard[AgentBlackboardKeys.RemediatedEvidencePacket] = JsonSerializer.SerializeToNode(packet, AgentNodeJson.SerializerOptions); source.BlackboardJson = sourceBoard.ToJsonString(AgentNodeJson.SerializerOptions); source.OutputJson = AgentNodeJson.Serialize(new EvidenceRemediationOutput("原回答", "修正版", "summary", "Supported", packet.Evidence, [], true, ["關鍵結論改變"])); db.AgentRuns.Add(source); await db.SaveChangesAsync();
        var service = new AgentRunService(db, [new EvidenceReanalysisWorkflowDefinitionProvider()], state, nodeState, queue);

        var summary = await service.CreateEvidenceReanalysisAsync(userId, source.Id, CancellationToken.None);

        Assert.Equal(AgentWorkflowTypes.EvidenceReanalysis, summary.WorkflowType); Assert.Equal(AgentTypes.Analysis, summary.AgentType); Assert.Equal(AgentRunStatuses.Pending, summary.Status); Assert.Single(queue.Messages);
        var detail = await service.GetByIdAsync(summary.Id, null, CancellationToken.None); Assert.NotNull(detail); Assert.Equal(7, detail.Nodes.Count);
        using var definition = JsonDocument.Parse(detail.WorkflowDefinitionJson); Assert.Equal("Stateful", definition.RootElement.GetProperty("orchestrationMode").GetString()); Assert.All(definition.RootElement.GetProperty("nodes").EnumerateArray(), node => Assert.True(node.TryGetProperty("executionPolicy", out _)));
    }

    [Fact]
    public async Task GetByIdAsync_DifferentUser_ReturnsNull()
    {
        await using var db = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var service = CreateService(
            db,
            new FakeResearchRunTraceService(BuildResearchRunDetail(Guid.NewGuid())),
            new DeterministicCriticReviewAgent());
        var summary = await service.CreateCriticReviewAsync(ownerId, Guid.NewGuid(), CancellationToken.None);

        var detail = await service.GetByIdAsync(summary.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.Null(detail);
    }

    [Fact]
    public void CriticReviewNodeHandlers_CoverEveryWorkflowNodeType()
    {
        var handlers = CreateCriticReviewHandlers(
            new FakeResearchRunTraceService(BuildResearchRunDetail(Guid.NewGuid())),
            new DeterministicCriticReviewAgent());

        var nodeTypes = new[]
        {
            CriticReviewNodeTypes.LoadResearchRun,
            CriticReviewNodeTypes.BuildEvidencePacket,
            CriticReviewNodeTypes.CheckEvidence,
            CriticReviewNodeTypes.CritiqueAnswer,
            CriticReviewNodeTypes.FinalizeCriticReport
        };

        Assert.All(nodeTypes, nodeType => Assert.Contains(handlers, handler => handler.NodeType == nodeType));
        Assert.Equal(nodeTypes.Length, handlers.Select(handler => handler.NodeType).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void DraftRevisionNodeHandlers_CoverEveryWorkflowNodeType()
    {
        var handlers = CreateAllHandlers(
            new FakeResearchRunTraceService(BuildResearchRunDetail(Guid.NewGuid())),
            new DeterministicCriticReviewAgent());

        var nodeTypes = new[]
        {
            DraftRevisionNodeTypes.LoadCriticReviewRun,
            DraftRevisionNodeTypes.DraftRevisedAnswer,
            DraftRevisionNodeTypes.FinalizeRevision
        };

        Assert.All(nodeTypes, nodeType => Assert.Contains(handlers, handler => handler.NodeType == nodeType));
    }

    [Fact]
    public async Task CreateCriticReviewAsync_MissingWorkflowProvider_Throws()
    {
        await using var db = CreateDbContext();
        var service = CreateServiceWithHandlers(
            db,
            [],
            CreateCriticReviewHandlers(
                new FakeResearchRunTraceService(BuildResearchRunDetail(Guid.NewGuid(), candidateCount: 3)),
                new DeterministicCriticReviewAgent()),
            new RecordingAgentRunQueue());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateCriticReviewAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));

        Assert.Equal($"Workflow provider '{AgentWorkflowTypes.CriticReview}' is not registered.", exception.Message);
    }

    [Fact]
    public async Task Constructor_DuplicateWorkflowProvider_Throws()
    {
        await using var db = CreateDbContext();

        var exception = Assert.Throws<InvalidOperationException>(() => CreateServiceWithHandlers(
            db,
            [new CriticReviewWorkflowDefinitionProvider(), new CriticReviewWorkflowDefinitionProvider()],
            CreateCriticReviewHandlers(
                new FakeResearchRunTraceService(BuildResearchRunDetail(Guid.NewGuid(), candidateCount: 3)),
                new DeterministicCriticReviewAgent()),
            new RecordingAgentRunQueue()));

        Assert.Equal($"Workflow provider '{AgentWorkflowTypes.CriticReview}' is registered more than once.", exception.Message);
    }

    [Fact]
    public async Task CreateCriticReviewAsync_MissingNodeHandler_MarksRunFailed()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var researchRunId = Guid.NewGuid();
        IAgentNodeHandler[] handlers =
        [
            new LoadResearchRunNodeHandler(new FakeResearchRunTraceService(BuildResearchRunDetail(researchRunId, candidateCount: 3))),
            new BuildEvidencePacketNodeHandler(),
            new CheckEvidenceNodeHandler(),
            new CritiqueAnswerNodeHandler(new DeterministicCriticReviewAgent())
        ];
        var service = CreateServiceWithHandlers(db, [new CriticReviewWorkflowDefinitionProvider()], handlers);

        var summary = await service.CreateCriticReviewAsync(userId, researchRunId, CancellationToken.None);

        Assert.Equal(AgentRunStatuses.Failed, summary.Status);
        Assert.Equal($"Unsupported node type '{CriticReviewNodeTypes.FinalizeCriticReport}'.", summary.ErrorMessage);
        var detail = await service.GetByIdAsync(summary.Id, userId, CancellationToken.None);
        Assert.NotNull(detail);
        Assert.Contains(detail.Nodes, n => n.NodeKey == CriticReviewNodeKeys.FinalizeCriticReport && n.Status == AgentNodeStatuses.Failed);
        Assert.Null(detail.OutputJson);
    }

    [Fact]
    public async Task CreateCriticReviewAsync_MissingPersistedNode_MarksRunFailed()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var researchRunId = Guid.NewGuid();
        var service = CreateServiceWithHandlers(
            db,
            [new MissingFinalizeNodeWorkflowDefinitionProvider()],
            CreateCriticReviewHandlers(
                new FakeResearchRunTraceService(BuildResearchRunDetail(researchRunId, candidateCount: 3)),
                new DeterministicCriticReviewAgent()));

        var summary = await service.CreateCriticReviewAsync(userId, researchRunId, CancellationToken.None);

        Assert.Equal(AgentRunStatuses.Failed, summary.Status);
        Assert.Equal($"Agent run is missing node '{CriticReviewNodeKeys.FinalizeCriticReport}'.", summary.ErrorMessage);
        var detail = await service.GetByIdAsync(summary.Id, userId, CancellationToken.None);
        Assert.NotNull(detail);
        Assert.Equal(4, detail.Nodes.Count);
        Assert.Null(detail.OutputJson);
    }

    private static void AssertCriticReviewBlackboardContract(AgentRunDetailResponse detail)
    {
        Assert.NotNull(detail.OutputJson);
        using var document = JsonDocument.Parse(detail.BlackboardJson);
        var root = document.RootElement;

        Assert.True(root.TryGetProperty(AgentBlackboardKeys.Ticker, out var ticker));
        Assert.Equal("2330", ticker.GetString());
        Assert.True(root.TryGetProperty(AgentBlackboardKeys.Question, out var question));
        Assert.Equal("question?", question.GetString());
        Assert.True(root.TryGetProperty(AgentBlackboardKeys.ResearchRunId, out var researchRunId));
        Assert.Equal(JsonValueKind.String, researchRunId.ValueKind);
        Assert.True(root.TryGetProperty(AgentBlackboardKeys.ResearchRun, out var researchRun));
        Assert.Equal(JsonValueKind.Object, researchRun.ValueKind);
        Assert.True(root.TryGetProperty(AgentBlackboardKeys.Answer, out var answer));
        Assert.Equal("answer [1]", answer.GetString());
        Assert.True(root.TryGetProperty(AgentBlackboardKeys.Citations, out var citations));
        Assert.Equal(JsonValueKind.Array, citations.ValueKind);
        Assert.True(root.TryGetProperty(AgentBlackboardKeys.Steps, out var steps));
        Assert.Equal(JsonValueKind.Array, steps.ValueKind);
        Assert.True(root.TryGetProperty(AgentBlackboardKeys.Candidates, out var candidates));
        Assert.Equal(JsonValueKind.Array, candidates.ValueKind);
        Assert.True(root.TryGetProperty(AgentBlackboardKeys.SupervisorDecisions, out var supervisorDecisions));
        Assert.Equal(JsonValueKind.Array, supervisorDecisions.ValueKind);

        Assert.True(root.TryGetProperty(AgentBlackboardKeys.EvidencePacket, out var evidencePacket));
        Assert.Equal(JsonValueKind.Object, evidencePacket.ValueKind);
        Assert.Equal("2330", evidencePacket.GetProperty(AgentBlackboardKeys.Ticker).GetString());
        Assert.Equal("question?", evidencePacket.GetProperty(AgentBlackboardKeys.Question).GetString());
        Assert.Equal("answer [1]", evidencePacket.GetProperty(AgentBlackboardKeys.Answer).GetString());
        Assert.Equal("Answered", evidencePacket.GetProperty("sourceStatus").GetString());
        Assert.Equal(1, evidencePacket.GetProperty(EvidenceCheckFields.CitationCount).GetInt32());
        Assert.Equal(1, evidencePacket.GetProperty(EvidenceCheckFields.CandidateCount).GetInt32());
        var packetSummary = evidencePacket.GetProperty("evidenceSummary");
        Assert.True(packetSummary.GetProperty("hasAnswer").GetBoolean());
        Assert.True(packetSummary.GetProperty("hasCitations").GetBoolean());
        Assert.Equal(0, packetSummary.GetProperty("emptyQuoteCount").GetInt32());
        Assert.Equal(1, packetSummary.GetProperty("selectedCandidateCount").GetInt32());

        Assert.True(root.TryGetProperty(AgentBlackboardKeys.EvidenceChecks, out var evidenceChecks));
        Assert.Equal(JsonValueKind.Object, evidenceChecks.ValueKind);
        Assert.Equal(1, evidenceChecks.GetProperty(EvidenceCheckFields.CitationCount).GetInt32());
        Assert.Equal(1, evidenceChecks.GetProperty(EvidenceCheckFields.CandidateCount).GetInt32());
        Assert.Equal("Answered", evidenceChecks.GetProperty(EvidenceCheckFields.SourceStatus).GetString());
        Assert.True(evidenceChecks.GetProperty(EvidenceCheckFields.FindingCount).GetInt32() > 0);
        Assert.Equal(JsonValueKind.Array, evidenceChecks.GetProperty(EvidenceCheckFields.Findings).ValueKind);

        Assert.True(root.TryGetProperty(AgentBlackboardKeys.CriticFindings, out var criticFindings));
        Assert.Equal(JsonValueKind.Array, criticFindings.ValueKind);
        Assert.True(root.TryGetProperty(AgentBlackboardKeys.CriticReview, out var criticReview));
        Assert.Equal(JsonValueKind.Object, criticReview.ValueKind);
        Assert.Equal("Medium", criticReview.GetProperty(CriticReviewFields.OverallSeverity).GetString());
        Assert.True(criticReview.GetProperty(CriticReviewFields.RequiresRevision).GetBoolean());
        Assert.True(criticReview.GetProperty(CriticReviewFields.RequiresMoreEvidence).GetBoolean());
        Assert.Equal("ResearchRetrieval", criticReview.GetProperty(CriticReviewFields.RouteBackTo).GetString());
        Assert.Equal("CollectMoreEvidenceThenReviseAnswer", criticReview.GetProperty(CriticReviewFields.RecommendedNextAction).GetString());

        Assert.True(root.TryGetProperty(AgentBlackboardKeys.FinalOutput, out var finalOutput));
        Assert.Equal(JsonValueKind.Object, finalOutput.ValueKind);
        Assert.True(finalOutput.GetProperty(CriticReviewFields.RequiresRevision).GetBoolean());
        Assert.True(finalOutput.GetProperty(CriticReviewFields.RequiresMoreEvidence).GetBoolean());
        Assert.Equal("ResearchRetrieval", finalOutput.GetProperty(CriticReviewFields.RouteBackTo).GetString());
        Assert.Equal("CollectMoreEvidenceThenReviseAnswer", finalOutput.GetProperty(CriticReviewFields.RecommendedNextAction).GetString());
        Assert.Equal(NormalizeJson(detail.OutputJson), JsonSerializer.Serialize(finalOutput));

        var loadNode = detail.Nodes.Single(n => n.NodeKey == CriticReviewNodeKeys.LoadResearchRun);
        Assert.NotNull(loadNode.InputJson);
        using var loadInput = JsonDocument.Parse(loadNode.InputJson);
        Assert.Equal(JsonValueKind.String, loadInput.RootElement.GetProperty("researchRunId").ValueKind);
        Assert.NotNull(loadNode.OutputJson);
        using var loadOutput = JsonDocument.Parse(loadNode.OutputJson);
        Assert.Equal("2330", loadOutput.RootElement.GetProperty(AgentBlackboardKeys.Ticker).GetString());
        Assert.Equal("Answered", loadOutput.RootElement.GetProperty("status").GetString());
        Assert.Equal(1, loadOutput.RootElement.GetProperty(EvidenceCheckFields.CitationCount).GetInt32());
        Assert.Equal(1, loadOutput.RootElement.GetProperty(EvidenceCheckFields.CandidateCount).GetInt32());
        Assert.True(loadOutput.RootElement.GetProperty("hasAnswer").GetBoolean());

        var buildEvidencePacketNode = detail.Nodes.Single(n => n.NodeKey == CriticReviewNodeKeys.BuildEvidencePacket);
        Assert.NotNull(buildEvidencePacketNode.InputJson);
        using var buildEvidencePacketInput = JsonDocument.Parse(buildEvidencePacketNode.InputJson);
        Assert.Equal("2330", buildEvidencePacketInput.RootElement.GetProperty(AgentBlackboardKeys.Ticker).GetString());
        Assert.Equal("Answered", buildEvidencePacketInput.RootElement.GetProperty("sourceStatus").GetString());
        Assert.Equal(1, buildEvidencePacketInput.RootElement.GetProperty(EvidenceCheckFields.CitationCount).GetInt32());
        Assert.Equal(1, buildEvidencePacketInput.RootElement.GetProperty(EvidenceCheckFields.CandidateCount).GetInt32());
        Assert.True(buildEvidencePacketInput.RootElement.GetProperty("hasAnswer").GetBoolean());
        Assert.NotNull(buildEvidencePacketNode.OutputJson);
        using var buildEvidencePacketOutput = JsonDocument.Parse(buildEvidencePacketNode.OutputJson);
        Assert.Equal("2330", buildEvidencePacketOutput.RootElement.GetProperty(AgentBlackboardKeys.Ticker).GetString());
        Assert.Equal("Answered", buildEvidencePacketOutput.RootElement.GetProperty("sourceStatus").GetString());
        Assert.Equal(1, buildEvidencePacketOutput.RootElement.GetProperty(EvidenceCheckFields.CitationCount).GetInt32());
        Assert.Equal(1, buildEvidencePacketOutput.RootElement.GetProperty(EvidenceCheckFields.CandidateCount).GetInt32());

        var checkEvidenceNode = detail.Nodes.Single(n => n.NodeKey == CriticReviewNodeKeys.CheckEvidence);
        Assert.NotNull(checkEvidenceNode.InputJson);
        using var checkEvidenceInput = JsonDocument.Parse(checkEvidenceNode.InputJson);
        Assert.Equal("2330", checkEvidenceInput.RootElement.GetProperty(AgentBlackboardKeys.Ticker).GetString());
        Assert.Equal("Answered", checkEvidenceInput.RootElement.GetProperty(EvidenceCheckFields.SourceStatus).GetString());
        Assert.Equal(1, checkEvidenceInput.RootElement.GetProperty(EvidenceCheckFields.CitationCount).GetInt32());
        Assert.Equal(1, checkEvidenceInput.RootElement.GetProperty(EvidenceCheckFields.CandidateCount).GetInt32());
        Assert.NotNull(checkEvidenceNode.OutputJson);
        using var checkEvidenceOutput = JsonDocument.Parse(checkEvidenceNode.OutputJson);
        Assert.Equal(1, checkEvidenceOutput.RootElement.GetProperty(EvidenceCheckFields.CitationCount).GetInt32());
        Assert.Equal(1, checkEvidenceOutput.RootElement.GetProperty(EvidenceCheckFields.CandidateCount).GetInt32());
        Assert.Equal("Answered", checkEvidenceOutput.RootElement.GetProperty(EvidenceCheckFields.SourceStatus).GetString());
        Assert.True(checkEvidenceOutput.RootElement.GetProperty(EvidenceCheckFields.FindingCount).GetInt32() > 0);
        Assert.Equal(JsonValueKind.Array, checkEvidenceOutput.RootElement.GetProperty(EvidenceCheckFields.Findings).ValueKind);

        var critiqueNode = detail.Nodes.Single(n => n.NodeKey == CriticReviewNodeKeys.CritiqueAnswer);
        Assert.NotNull(critiqueNode.InputJson);
        Assert.NotNull(critiqueNode.OutputJson);
        using var critiqueInput = JsonDocument.Parse(critiqueNode.InputJson);
        Assert.Equal("2330", critiqueInput.RootElement.GetProperty(AgentBlackboardKeys.Ticker).GetString());
        Assert.Equal("answer [1]", critiqueInput.RootElement.GetProperty(AgentBlackboardKeys.Answer).GetString());
        using var critiqueOutput = JsonDocument.Parse(critiqueNode.OutputJson);
        Assert.Equal("Medium", critiqueOutput.RootElement.GetProperty(CriticReviewFields.OverallSeverity).GetString());
        Assert.Equal(JsonValueKind.Array, critiqueOutput.RootElement.GetProperty(CriticReviewFields.Findings).ValueKind);

        var finalizeNode = detail.Nodes.Single(n => n.NodeKey == CriticReviewNodeKeys.FinalizeCriticReport);
        Assert.NotNull(finalizeNode.InputJson);
        using var finalizeInput = JsonDocument.Parse(finalizeNode.InputJson);
        Assert.Equal("Medium", finalizeInput.RootElement.GetProperty(CriticReviewFields.OverallSeverity).GetString());
        Assert.True(finalizeInput.RootElement.GetProperty(CriticReviewFields.RequiresRevision).GetBoolean());
        Assert.True(finalizeInput.RootElement.GetProperty(CriticReviewFields.RequiresMoreEvidence).GetBoolean());
        Assert.Equal("ResearchRetrieval", finalizeInput.RootElement.GetProperty(CriticReviewFields.RouteBackTo).GetString());
        Assert.Equal("CollectMoreEvidenceThenReviseAnswer", finalizeInput.RootElement.GetProperty(CriticReviewFields.RecommendedNextAction).GetString());
        Assert.NotNull(finalizeNode.OutputJson);
        Assert.Equal(NormalizeJson(detail.OutputJson), NormalizeJson(finalizeNode.OutputJson));
    }

    private static string NormalizeJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }

    private static void AssertCriticReviewWorkflowDefinition(string workflowDefinitionJson)
    {
        using var document = JsonDocument.Parse(workflowDefinitionJson);
        var root = document.RootElement;
        Assert.Equal(AgentWorkflowTypes.CriticReview, root.GetProperty("workflowType").GetString());
        Assert.Equal(CriticReviewWorkflow.Version, root.GetProperty("version").GetInt32());

        var nodes = root.GetProperty("nodes").EnumerateArray().ToArray();
        Assert.Equal(5, nodes.Length);
        Assert.Equal(CriticReviewNodeKeys.LoadResearchRun, nodes[0].GetProperty("id").GetString());
        Assert.Equal(CriticReviewNodeTypes.LoadResearchRun, nodes[0].GetProperty("type").GetString());
        Assert.True(nodes[0].GetProperty("required").GetBoolean());
        Assert.Equal(CriticReviewNodeKeys.BuildEvidencePacket, nodes[1].GetProperty("id").GetString());
        Assert.Equal(CriticReviewNodeTypes.BuildEvidencePacket, nodes[1].GetProperty("type").GetString());
        Assert.True(nodes[1].GetProperty("required").GetBoolean());
        Assert.Equal(CriticReviewNodeKeys.CheckEvidence, nodes[2].GetProperty("id").GetString());
        Assert.Equal(CriticReviewNodeTypes.CheckEvidence, nodes[2].GetProperty("type").GetString());
        Assert.True(nodes[2].GetProperty("required").GetBoolean());
        Assert.Equal(CriticReviewNodeKeys.CritiqueAnswer, nodes[3].GetProperty("id").GetString());
        Assert.Equal(CriticReviewNodeTypes.CritiqueAnswer, nodes[3].GetProperty("type").GetString());
        Assert.True(nodes[3].GetProperty("required").GetBoolean());
        Assert.Equal(CriticReviewNodeKeys.FinalizeCriticReport, nodes[4].GetProperty("id").GetString());
        Assert.Equal(CriticReviewNodeTypes.FinalizeCriticReport, nodes[4].GetProperty("type").GetString());
        Assert.True(nodes[4].GetProperty("required").GetBoolean());

        var edges = root.GetProperty("edges").EnumerateArray().ToArray();
        Assert.Equal(4, edges.Length);
        Assert.Equal(CriticReviewNodeKeys.LoadResearchRun, edges[0].GetProperty("from").GetString());
        Assert.Equal(CriticReviewNodeKeys.BuildEvidencePacket, edges[0].GetProperty("to").GetString());
        Assert.Equal(CriticReviewNodeKeys.BuildEvidencePacket, edges[1].GetProperty("from").GetString());
        Assert.Equal(CriticReviewNodeKeys.CheckEvidence, edges[1].GetProperty("to").GetString());
        Assert.Equal(CriticReviewNodeKeys.CheckEvidence, edges[2].GetProperty("from").GetString());
        Assert.Equal(CriticReviewNodeKeys.CritiqueAnswer, edges[2].GetProperty("to").GetString());
        Assert.Equal(CriticReviewNodeKeys.CritiqueAnswer, edges[3].GetProperty("from").GetString());
        Assert.Equal(CriticReviewNodeKeys.FinalizeCriticReport, edges[3].GetProperty("to").GetString());
    }

    private static void AssertResearchQualityReviewWorkflowDefinition(string workflowDefinitionJson)
    {
        using var document = JsonDocument.Parse(workflowDefinitionJson);
        var root = document.RootElement;
        Assert.Equal(AgentWorkflowTypes.ResearchQualityReview, root.GetProperty("workflowType").GetString());
        Assert.Equal(ResearchQualityReviewWorkflow.Version, root.GetProperty("version").GetInt32());

        var nodes = root.GetProperty("nodes").EnumerateArray().ToArray();
        Assert.Equal(7, nodes.Length);
        Assert.Equal(ResearchQualityReviewNodeKeys.LoadResearchRun, nodes[0].GetProperty("id").GetString());
        Assert.Equal(ResearchQualityReviewNodeTypes.LoadResearchRun, nodes[0].GetProperty("type").GetString());
        Assert.Equal(ResearchQualityReviewNodeKeys.BuildEvidencePacket, nodes[1].GetProperty("id").GetString());
        Assert.Equal(ResearchQualityReviewNodeTypes.BuildEvidencePacket, nodes[1].GetProperty("type").GetString());
        Assert.Equal(ResearchQualityReviewNodeKeys.CheckEvidence, nodes[2].GetProperty("id").GetString());
        Assert.Equal(ResearchQualityReviewNodeTypes.CheckEvidence, nodes[2].GetProperty("type").GetString());
        Assert.Equal(ResearchQualityReviewNodeKeys.CritiqueAnswer, nodes[3].GetProperty("id").GetString());
        Assert.Equal(ResearchQualityReviewNodeTypes.CritiqueAnswer, nodes[3].GetProperty("type").GetString());
        Assert.Equal(ResearchQualityReviewNodeKeys.FinalizeCriticReport, nodes[4].GetProperty("id").GetString());
        Assert.Equal(ResearchQualityReviewNodeTypes.FinalizeCriticReport, nodes[4].GetProperty("type").GetString());
        Assert.Equal(ResearchQualityReviewNodeKeys.DraftRevisedAnswer, nodes[5].GetProperty("id").GetString());
        Assert.Equal(ResearchQualityReviewNodeTypes.DraftRevisedAnswer, nodes[5].GetProperty("type").GetString());
        Assert.Equal(ResearchQualityReviewNodeKeys.FinalizeRevision, nodes[6].GetProperty("id").GetString());
        Assert.Equal(ResearchQualityReviewNodeTypes.FinalizeRevision, nodes[6].GetProperty("type").GetString());

        var edges = root.GetProperty("edges").EnumerateArray().ToArray();
        Assert.Equal(6, edges.Length);
        Assert.Equal(ResearchQualityReviewNodeKeys.LoadResearchRun, edges[0].GetProperty("from").GetString());
        Assert.Equal(ResearchQualityReviewNodeKeys.BuildEvidencePacket, edges[0].GetProperty("to").GetString());
        Assert.Equal(ResearchQualityReviewNodeKeys.BuildEvidencePacket, edges[1].GetProperty("from").GetString());
        Assert.Equal(ResearchQualityReviewNodeKeys.CheckEvidence, edges[1].GetProperty("to").GetString());
        Assert.Equal(ResearchQualityReviewNodeKeys.CheckEvidence, edges[2].GetProperty("from").GetString());
        Assert.Equal(ResearchQualityReviewNodeKeys.CritiqueAnswer, edges[2].GetProperty("to").GetString());
        Assert.Equal(ResearchQualityReviewNodeKeys.CritiqueAnswer, edges[3].GetProperty("from").GetString());
        Assert.Equal(ResearchQualityReviewNodeKeys.FinalizeCriticReport, edges[3].GetProperty("to").GetString());
        Assert.Equal(ResearchQualityReviewNodeKeys.FinalizeCriticReport, edges[4].GetProperty("from").GetString());
        Assert.Equal(ResearchQualityReviewNodeKeys.DraftRevisedAnswer, edges[4].GetProperty("to").GetString());
        Assert.Equal(ResearchQualityReviewNodeKeys.DraftRevisedAnswer, edges[5].GetProperty("from").GetString());
        Assert.Equal(ResearchQualityReviewNodeKeys.FinalizeRevision, edges[5].GetProperty("to").GetString());
    }

    private static AgentRunService CreateService(
        EquityLensDbContext db,
        IResearchRunTraceService researchTraceService,
        ICriticReviewAgent criticReviewAgent,
        IAgentRunStateMachine? runStateMachine = null,
        IAgentNodeStateMachine? nodeStateMachine = null,
        IAgentRunQueue? agentRunQueue = null,
        IAgentWorkflowAdminService? workflowAdminService = null)
    {
        runStateMachine ??= new AgentRunStateMachine();
        nodeStateMachine ??= new AgentNodeStateMachine();
        var handlers = CreateAllHandlers(researchTraceService, criticReviewAgent);
        agentRunQueue ??= new AutoExecutingAgentRunQueue(db, handlers, runStateMachine, nodeStateMachine);

        return new AgentRunService(
            db,
            [new CriticReviewWorkflowDefinitionProvider(), new DraftRevisionWorkflowDefinitionProvider(), new ResearchQualityReviewWorkflowDefinitionProvider()],
            runStateMachine,
            nodeStateMachine,
            agentRunQueue,
            workflowAdminService);
    }

    private static AgentRunService CreateServiceWithHandlers(
        EquityLensDbContext db,
        IEnumerable<IAgentWorkflowDefinitionProvider> workflowProviders,
        IEnumerable<IAgentNodeHandler> handlers,
        IAgentRunQueue? agentRunQueue = null,
        IAgentRunStateMachine? runStateMachine = null,
        IAgentNodeStateMachine? nodeStateMachine = null)
    {
        runStateMachine ??= new AgentRunStateMachine();
        nodeStateMachine ??= new AgentNodeStateMachine();
        agentRunQueue ??= new AutoExecutingAgentRunQueue(db, handlers, runStateMachine, nodeStateMachine);

        return new AgentRunService(
            db,
            workflowProviders,
            runStateMachine,
            nodeStateMachine,
            agentRunQueue);
    }

    private static IAgentNodeHandler[] CreateCriticReviewHandlers(
        IResearchRunTraceService researchTraceService,
        ICriticReviewAgent criticReviewAgent) =>
    [
        new LoadResearchRunNodeHandler(researchTraceService),
        new BuildEvidencePacketNodeHandler(),
        new CheckEvidenceNodeHandler(),
        new CritiqueAnswerNodeHandler(criticReviewAgent),
        new FinalizeCriticReportNodeHandler([new CriticReviewPolicyEvaluator(), new ResearchQualityReviewPolicyEvaluator()])
    ];

    private static IAgentNodeHandler[] CreateAllHandlers(
        IResearchRunTraceService researchTraceService,
        ICriticReviewAgent criticReviewAgent) =>
    [
        new LoadResearchRunNodeHandler(researchTraceService),
        new BuildEvidencePacketNodeHandler(),
        new CheckEvidenceNodeHandler(),
        new CritiqueAnswerNodeHandler(criticReviewAgent),
        new FinalizeCriticReportNodeHandler([new CriticReviewPolicyEvaluator(), new ResearchQualityReviewPolicyEvaluator()]),
        new LoadCriticReviewRunNodeHandler(),
        new DraftRevisedAnswerNodeHandler(new DeterministicDraftRevisionAgent()),
        new FinalizeRevisionNodeHandler()
    ];

    private static EquityLensDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<EquityLensDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestEquityLensDbContext(options);
    }

    private sealed class AutoExecutingAgentRunQueue : IAgentRunQueue
    {
        private readonly IAgentRunExecutor _executor;
        private readonly EquityLensDbContext _db;

        public AutoExecutingAgentRunQueue(
            EquityLensDbContext db,
            IEnumerable<IAgentNodeHandler> handlers,
            IAgentRunStateMachine runStateMachine,
            IAgentNodeStateMachine nodeStateMachine)
        {
            _db = db;
            _executor = new AgentRunExecutor(
                db,
                new AgentWorkflowPlanner(),
                new AgentRunGraphValidator(),
                runStateMachine,
                nodeStateMachine,
                handlers,
                NullLogger<AgentRunExecutor>.Instance);
        }

        public async Task EnqueueAsync(AgentRunQueueMessage message, CancellationToken cancellationToken = default)
        {
            for (var wake = 0; wake < 100; wake++)
            {
                await _executor.ExecuteAsync(message.RunId, message.UserId, cancellationToken);
                var status = await _db.AgentRuns.Where(x => x.Id == message.RunId).Select(x => x.Status).SingleAsync(cancellationToken);
                if (status is AgentRunStatuses.Succeeded or AgentRunStatuses.Failed or AgentRunStatuses.Cancelled) return;
            }
            throw new InvalidOperationException("Auto executing queue exceeded 100 orchestration wakes.");
        }

        public Task<AgentRunQueueItem?> ReadNextAsync(string consumerName, CancellationToken cancellationToken = default) =>
            Task.FromResult<AgentRunQueueItem?>(null);

        public Task<AgentRunQueueItem?> ReadStalePendingAsync(
            string consumerName,
            TimeSpan minIdleTime,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AgentRunQueueItem?>(null);

        public Task AcknowledgeAsync(string streamId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class RecordingAgentRunQueue : IAgentRunQueue
    {
        public List<AgentRunQueueMessage> Messages { get; } = [];

        public Task EnqueueAsync(AgentRunQueueMessage message, CancellationToken cancellationToken = default)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }

        public Task<AgentRunQueueItem?> ReadNextAsync(string consumerName, CancellationToken cancellationToken = default) =>
            Task.FromResult<AgentRunQueueItem?>(null);

        public Task<AgentRunQueueItem?> ReadStalePendingAsync(
            string consumerName,
            TimeSpan minIdleTime,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AgentRunQueueItem?>(null);

        public Task AcknowledgeAsync(string streamId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class RecordingRunStateMachine : IAgentRunStateMachine
    {
        private readonly AgentRunStateMachine _inner = new();

        public List<StatusTransition> Transitions { get; } = [];

        public void Transition(AgentRun run, string nextStatus)
        {
            var from = run.Status;
            _inner.Transition(run, nextStatus);
            Transitions.Add(new StatusTransition(from, nextStatus));
        }

        public void ResetForRetry(AgentRun run)
        {
            var from = run.Status;
            _inner.ResetForRetry(run);
            Transitions.Add(new StatusTransition(from, run.Status));
        }
    }

    private sealed class RecordingNodeStateMachine : IAgentNodeStateMachine
    {
        private readonly AgentNodeStateMachine _inner = new();

        public List<NodeStatusTransition> Transitions { get; } = [];

        public void Transition(AgentRunNode node, string nextStatus)
        {
            var from = node.Status;
            _inner.Transition(node, nextStatus);
            Transitions.Add(new NodeStatusTransition(node.NodeKey, from, nextStatus));
        }

        public void ResetForRetry(AgentRunNode node)
        {
            var from = node.Status;
            _inner.ResetForRetry(node);
            if (!string.Equals(from, node.Status, StringComparison.Ordinal))
            {
                Transitions.Add(new NodeStatusTransition(node.NodeKey, from, node.Status));
            }
        }
    }

    private sealed record StatusTransition(string From, string To);

    private sealed record NodeStatusTransition(string NodeKey, string From, string To);

    private sealed class MissingFinalizeNodeWorkflowDefinitionProvider : IAgentWorkflowDefinitionProvider
    {
        private readonly CriticReviewWorkflowDefinitionProvider _inner = new();

        public string WorkflowType => _inner.WorkflowType;

        public AgentRun CreateRun(Guid userId, Guid researchRunId)
        {
            var run = _inner.CreateRun(userId, researchRunId);
            run.Nodes = run.Nodes
                .Where(node => node.NodeKey != CriticReviewNodeKeys.FinalizeCriticReport)
                .ToList();
            return run;
        }

        public string CreateInitialBlackboardJson(Guid researchRunId) => _inner.CreateInitialBlackboardJson(researchRunId);
    }

    private sealed class TestEquityLensDbContext : EquityLensDbContext
    {
        public TestEquityLensDbContext(DbContextOptions<EquityLensDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Ignore<DocumentEmbedding>();
            modelBuilder.Entity<AgentRun>().Property(x => x.Id).ValueGeneratedNever();
            modelBuilder.Entity<AgentRunNode>().Property(x => x.Id).ValueGeneratedNever();
            modelBuilder.Entity<AgentRunEvent>().Property(x => x.Id).ValueGeneratedNever();
            modelBuilder.Entity<AgentToolCall>().Property(x => x.Id).ValueGeneratedNever();
            modelBuilder.Entity<AgentFeedback>().Property(x => x.Id).ValueGeneratedNever();
        }
    }

    private static ResearchRunDetailDto BuildResearchRunDetail(Guid id, int candidateCount = 1)
    {
        var chunkId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var candidates = Enumerable.Range(1, candidateCount)
            .Select(rank => new ResearchRunCandidateDto(
                Guid.NewGuid(),
                rank == 1 ? chunkId : Guid.NewGuid(),
                rank == 1 ? documentId : Guid.NewGuid(),
                "年報",
                "AnnualReport",
                null,
                rank,
                0.9,
                null,
                1,
                rank,
                "Selected",
                null,
                "content"))
            .ToArray();

        return new ResearchRunDetailDto(
            new ResearchRunSummaryDto(id, "2330", "question?", "Answered", 1, "Auto", 123, DateTime.UtcNow),
            "answer [1]",
            [new ResearchRunStepDto(Guid.NewGuid(), "AnswerGeneration", null, null, 10, DateTime.UtcNow, DateTime.UtcNow, null)],
            candidates,
            [new ResearchRunCitationDto(Guid.NewGuid(), 1, "LocalDocument", chunkId, documentId, "年報", "AnnualReport", null, 1, "quote", 0.9)])
        { };
    }

    private sealed class FlakyCriticReviewAgent : ICriticReviewAgent
    {
        private readonly DeterministicCriticReviewAgent _inner = new();

        public int CallCount { get; private set; }

        public Task<CriticReviewResult> CritiqueAsync(CriticReviewInput input, CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (CallCount == 1)
            {
                throw new InvalidOperationException("critic unavailable");
            }

            return _inner.CritiqueAsync(input, cancellationToken);
        }
    }

    private sealed class NoFindingCriticReviewAgent : ICriticReviewAgent
    {
        public Task<CriticReviewResult> CritiqueAsync(CriticReviewInput input, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CriticReviewResult(
                "未發現額外問題。",
                "None",
                [],
                null));
    }

    private sealed class FakeResearchRunTraceService : IResearchRunTraceService
    {
        private readonly ResearchRunDetailDto? _detail;

        public FakeResearchRunTraceService(ResearchRunDetailDto? detail)
        {
            _detail = detail;
        }

        public Task<Guid> PersistAskAsync(Guid userId, ResearchAskRequest request, ResearchAskResponse response, IReadOnlyList<StepInput>? steps = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Guid.Empty);

        public Task<IReadOnlyList<ResearchRunSummaryDto>> ListAsync(Guid? userId, int limit = 50, string? ticker = null, string? status = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ResearchRunSummaryDto>>([]);

        public Task<ResearchRunDetailDto?> GetByIdAsync(Guid id, Guid? userId, CancellationToken cancellationToken = default)
            => Task.FromResult(_detail);
    }
}
