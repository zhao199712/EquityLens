using EquityLens.Api.Services.Agents.Loop;
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
    public void ResearchAgentPolicyEvaluator_EvidenceFinding_SetsRouteBackToNull()
    {
        var evaluator = new ResearchAgentPolicyEvaluator();
        var criticReview = new JsonObject
        {
            [CriticReviewFields.Findings] = new JsonArray
            {
                AgentBlackboardContracts.CreateFinding("High", "WeakCitation", "引用不足。", "補強引用。")
            }
        };

        var decision = evaluator.Evaluate(new WorkflowPolicyContext(
            AgentWorkflowTypes.ResearchAgent,
            CriticReviewNodeKeys.FinalizeCriticReport,
            new JsonObject(),
            criticReview));

        Assert.True(decision.ShouldStop);
        Assert.True(decision.RequiresRevision);
        Assert.True(decision.RequiresMoreEvidence);
        Assert.Null(decision.RouteBackTo);
        Assert.Equal("ReviseAnswer", decision.RecommendedNextAction);
    }

    [Fact]
    public void ResearchAgentPolicyEvaluator_NoFindings_AcceptsAnswer()
    {
        var evaluator = new ResearchAgentPolicyEvaluator();
        var criticReview = new JsonObject
        {
            [CriticReviewFields.Findings] = new JsonArray()
        };

        var decision = evaluator.Evaluate(new WorkflowPolicyContext(
            AgentWorkflowTypes.ResearchAgent,
            CriticReviewNodeKeys.FinalizeCriticReport,
            new JsonObject(),
            criticReview));

        Assert.True(decision.ShouldStop);
        Assert.False(decision.RequiresRevision);
        Assert.Equal("AcceptAnswer", decision.RecommendedNextAction);
    }

    [Fact]
    public void ResearchAgentPolicyEvaluator_DeterministicEvidencePass_OverridesMediumMissingCitation()
    {
        var board = new JsonObject
        {
            [AgentBlackboardKeys.EvidencePacket] = new JsonObject { ["evidenceSummary"] = new JsonObject { ["hasAnswer"] = true, ["hasCitations"] = true, ["emptyQuoteCount"] = 0 } },
            [AgentBlackboardKeys.EvidenceChecks] = new JsonObject { [EvidenceCheckFields.FindingCount] = 0 }
        };
        var review = new JsonObject
        {
            [CriticReviewFields.OverallSeverity] = "Medium",
            [CriticReviewFields.Findings] = new JsonArray { AgentBlackboardContracts.CreateFinding("Medium", "MissingCitation", "LLM claimed citation text was missing.", "Provide quotes.") }
        };

        var decision = new ResearchAgentPolicyEvaluator().Evaluate(new(AgentWorkflowTypes.ResearchAgent, "finalizeCriticReport:1", board, review));

        Assert.Equal("AcceptAnswer", decision.RecommendedNextAction);
        Assert.False(decision.RequiresRevision);
        Assert.False(decision.RequiresMoreEvidence);
    }

    [Fact]
    public void ResearchAgentPolicyEvaluator_HighFinding_DoesNotUseFastPath()
    {
        var board = new JsonObject
        {
            [AgentBlackboardKeys.EvidencePacket] = new JsonObject { ["evidenceSummary"] = new JsonObject { ["hasAnswer"] = true, ["hasCitations"] = true, ["emptyQuoteCount"] = 0 } },
            [AgentBlackboardKeys.EvidenceChecks] = new JsonObject { [EvidenceCheckFields.FindingCount] = 0 }
        };
        var review = new JsonObject
        {
            [CriticReviewFields.OverallSeverity] = "High",
            [CriticReviewFields.Findings] = new JsonArray { AgentBlackboardContracts.CreateFinding("High", "InsufficientEvidence", "Material gap.", "Retrieve evidence.") }
        };

        var decision = new ResearchAgentPolicyEvaluator().Evaluate(new(AgentWorkflowTypes.ResearchAgent, "finalizeCriticReport:1", board, review));

        Assert.True(decision.RequiresMoreEvidence);
    }



    [Fact]
    public void ResearchAgentPolicyEvaluator_NonEvidenceFinding_SetsRouteBackToNull()
    {
        var evaluator = new ResearchAgentPolicyEvaluator();
        var criticReview = new JsonObject
        {
            [CriticReviewFields.Findings] = new JsonArray
            {
                AgentBlackboardContracts.CreateFinding("Medium", "UnsupportedClaim", "回答有未支撐推論。", "重寫回答。")
            }
        };

        var decision = evaluator.Evaluate(new WorkflowPolicyContext(
            AgentWorkflowTypes.ResearchAgent,
            CriticReviewNodeKeys.FinalizeCriticReport,
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
        Assert.Equal(3, detail.Nodes.Count);
        Assert.Contains(detail.Nodes, n => n.NodeKey == "qualityGate" && n.Status == AgentNodeStatuses.Succeeded);
        var critiqueNode = Assert.Single(detail.Nodes, n => n.NodeKey == "qualityGate");
        Assert.Equal("品質檢查", critiqueNode.DisplayName);
        Assert.Equal("Validation", critiqueNode.Stage);
        Assert.False(string.IsNullOrWhiteSpace(critiqueNode.Description));
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
            "qualityGate",
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
        Assert.Contains(failedDetail.Nodes, n => n.NodeKey == "qualityGate" && n.Status == AgentNodeStatuses.Failed);
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
        Assert.Contains(retriedDetail.Nodes, n => n.NodeKey == "qualityGate" && n.Status == AgentNodeStatuses.Succeeded);
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
            EquityLens.Api.Services.Agents.Loop.QualityGateNodeHandler.Type,
            CriticReviewNodeTypes.FinalizeCriticReport
        };

        Assert.All(nodeTypes, nodeType => Assert.Contains(handlers, handler => handler.NodeType == nodeType));
        Assert.Equal(nodeTypes.Length, handlers.Select(handler => handler.NodeType).Distinct(StringComparer.Ordinal).Count());
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


            new EquityLens.Api.Services.Agents.Loop.QualityGateNodeHandler(new DeterministicCriticReviewAgent(), [new CriticReviewPolicyEvaluator()])
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
        Assert.Equal(2, detail.Nodes.Count);
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

        var buildEvidencePacketNode = new { InputJson = root.GetProperty("qualityGate:qualityGate:0").GetProperty("packet").GetProperty("input").GetRawText(), OutputJson = root.GetProperty("qualityGate:qualityGate:0").GetProperty("packet").GetProperty("output").GetRawText() };
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

        var checkEvidenceNode = new { InputJson = root.GetProperty("qualityGate:qualityGate:0").GetProperty("evidence").GetProperty("input").GetRawText(), OutputJson = root.GetProperty("qualityGate:qualityGate:0").GetProperty("evidence").GetProperty("output").GetRawText() };
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

        var critiqueNode = new { InputJson = root.GetProperty("qualityGate:qualityGate:0").GetProperty("critic").GetProperty("input").GetRawText(), OutputJson = root.GetProperty("qualityGate:qualityGate:0").GetProperty("critic").GetProperty("output").GetRawText() };
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
        var root = JsonNode.Parse(workflowDefinitionJson)!;
        Assert.Equal(AgentWorkflowTypes.CriticReview, root["workflowType"]!.GetValue<string>());
        Assert.Equal(CriticReviewWorkflow.Version, root["version"]!.GetValue<int>());
        var nodes = root["nodes"]!.AsArray();
        Assert.Equal(new[] { "loadResearchRun", "qualityGate", "finalizeCriticReport" }, nodes.Select(n => n!["id"]!.GetValue<string>()));
        Assert.Equal(new[] { "LoadResearchRun", "QualityGate", "FinalizeCriticReport" }, nodes.Select(n => n!["type"]!.GetValue<string>()));
        Assert.All(nodes, n => Assert.True(n!["required"]!.GetValue<bool>()));
        Assert.Equal(new[] { "loadResearchRun->qualityGate", "qualityGate->finalizeCriticReport" },
            root["edges"]!.AsArray().Select(e => e!["from"]!.GetValue<string>() + "->" + e["to"]!.GetValue<string>()));
    }



    [Fact]
    public async Task SubmitFeedbackAsync_NeedsCorrection_CreatesImmutableChildRunAndResearchArtifact()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var sourceResearch = new ResearchRun
        {
            Id = Guid.NewGuid(), UserId = userId, TraceId = Guid.NewGuid().ToString("N"), Ticker = "2454",
            Question = "聯發科的獲利表現如何？", Answer = "原始答案 [1]", Status = "Answered",
            RetrievalMode = "Auto", SourcePolicy = "Auto", TopK = 8, Temperature = 0.1
        };
        var sourceRun = new AgentRun
        {
            Id = Guid.NewGuid(), UserId = userId, ResearchRunId = sourceResearch.Id,
            WorkflowType = AgentWorkflowTypes.ResearchAgent, AgentType = AgentTypes.Research,
            Status = AgentRunStatuses.Succeeded, InputJson = "{}", BlackboardJson = "{}",
            WorkflowDefinitionJson = "{\"nodes\":[]}", OutputJson = "{\"answer\":\"原始答案 [1]\"}"
        };
        db.ResearchRuns.Add(sourceResearch); db.AgentRuns.Add(sourceRun); await db.SaveChangesAsync();
        var queue = new RecordingAgentRunQueue();
        var service = CreateServiceWithHandlers(db, [new EquityLens.Api.Services.Agents.Loop.ResearchAgentWorkflowDefinitionProvider()], [], queue);
        var requestId = Guid.NewGuid();

        var created = await service.SubmitFeedbackAsync(sourceRun.Id, userId,
            new SubmitAgentFeedbackRequest(requestId, AgentFeedbackTypes.NeedsCorrection, "請重新檢查財報數字與引用來源。"));
        var repeated = await service.SubmitFeedbackAsync(sourceRun.Id, userId,
            new SubmitAgentFeedbackRequest(requestId, AgentFeedbackTypes.NeedsCorrection, "請重新檢查財報數字與引用來源。"));

        Assert.NotNull(created.FollowUpAgentRun);
        Assert.Equal(created.FollowUpAgentRun.Id, repeated.FollowUpAgentRun!.Id);
        Assert.Equal(sourceRun.Id, created.FollowUpAgentRun.ParentAgentRunId);
        Assert.Equal(AgentWorkflowTypes.ResearchAgent, created.FollowUpAgentRun.WorkflowType);
        var child = await db.AgentRuns.Include(x => x.Nodes).SingleAsync(x => x.Id == created.FollowUpAgentRun.Id);
        Assert.Equal(new[] { "reviseAgent", "reviseQualityGate", "finalizeResearch" }, new WorkflowGraphTopologyService().GetExecutionOrder(child.WorkflowDefinitionJson));
        var childBoard = AgentNodeJson.ParseBlackboard(child.BlackboardJson);
        Assert.Equal("請重新檢查財報數字與引用來源。", childBoard[AgentBlackboardKeys.FeedbackComment]!.GetValue<string>());
        Assert.Equal(sourceResearch.Answer, childBoard[AgentBlackboardKeys.OriginalAnswer]!.GetValue<string>());
        var originalBoard = child.BlackboardJson;
        new AgentRunStateMachine().Transition(child, AgentRunStatuses.Running);
        new AgentRunStateMachine().Transition(child, AgentRunStatuses.Failed);
        await db.SaveChangesAsync();
        await service.RetryAsync(child.Id, userId);
        Assert.Equal(originalBoard, child.BlackboardJson);
        Assert.Equal(AgentRunStatuses.Pending, created.FollowUpAgentRun.Status);
        Assert.Equal(2, await db.AgentRuns.CountAsync());
        Assert.Single(await db.AgentFeedback.ToListAsync());
        var childResearch = await db.ResearchRuns.SingleAsync(x => x.Id == created.FollowUpResearchRunId);
        Assert.Equal(sourceResearch.Id, childResearch.ParentResearchRunId);
        Assert.Equal(created.Feedback.Id, childResearch.RevisionFeedbackId);
        Assert.Equal("Pending", childResearch.Status);
        Assert.Equal(2, queue.Messages.Count);
        var children = await service.ListChildrenAsync(sourceRun.Id, userId);
        Assert.Single(children);
        Assert.Equal(created.FollowUpAgentRun.Id, children[0].Id);
    }

    [Fact]
    public async Task SubmitFeedbackAsync_Helpful_RecordsFeedbackWithoutStartingChildRun()
    {
        await using var db = CreateDbContext(); var userId = Guid.NewGuid();
        var sourceRun = new AgentRun
        {
            Id = Guid.NewGuid(), UserId = userId, WorkflowType = AgentWorkflowTypes.PortfolioDiagnosis,
            AgentType = AgentTypes.Analysis, Status = AgentRunStatuses.Succeeded, InputJson = "{}",
            BlackboardJson = "{}", WorkflowDefinitionJson = "{\"nodes\":[]}", OutputJson = "{}"
        };
        db.AgentRuns.Add(sourceRun); await db.SaveChangesAsync();
        var queue = new RecordingAgentRunQueue();
        var service = new AgentRunService(db, [new ResearchAgentWorkflowDefinitionProvider()], new AgentRunStateMachine(), new AgentNodeStateMachine(), queue);

        var result = await service.SubmitFeedbackAsync(sourceRun.Id, userId,
            new SubmitAgentFeedbackRequest(Guid.NewGuid(), AgentFeedbackTypes.Helpful, null));

        Assert.Null(result.FollowUpAgentRun);
        Assert.Null(result.FollowUpResearchRunId);
        Assert.Empty(queue.Messages);
        Assert.Single(await db.AgentFeedback.ToListAsync());
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
            [new CriticReviewWorkflowDefinitionProvider(), new ResearchAgentWorkflowDefinitionProvider(), new PortfolioDiagnosisWorkflowDefinitionProvider()],
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
        new EquityLens.Api.Services.Agents.Loop.QualityGateNodeHandler(criticReviewAgent, [new CriticReviewPolicyEvaluator()]),
        new FinalizeCriticReportNodeHandler([new CriticReviewPolicyEvaluator(), new ResearchAgentPolicyEvaluator()])
    ];

    private static IAgentNodeHandler[] CreateAllHandlers(
        IResearchRunTraceService researchTraceService,
        ICriticReviewAgent criticReviewAgent) =>
    [
        new LoadResearchRunNodeHandler(researchTraceService),



        new EquityLens.Api.Services.Agents.Loop.QualityGateNodeHandler(criticReviewAgent, [new CriticReviewPolicyEvaluator()]),
        new FinalizeCriticReportNodeHandler([new CriticReviewPolicyEvaluator(), new ResearchAgentPolicyEvaluator()]),
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
            _executor = AgentExecutorTestFactory.Create(
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
