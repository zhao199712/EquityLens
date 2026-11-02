using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.Agents.Loop;
using EquityLens.Api.Services.Ai;
using EquityLens.Api.Services.Ai.Retrieval;
using EquityLens.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class ResearchAgentWorkflowTests
{
    [Theory]
    [InlineData(null, AgentWorkflowTypes.ResearchInvestigation)]
    [InlineData("DynamicDag", AgentWorkflowTypes.ResearchInvestigation)]
    [InlineData("AgentLoop", AgentWorkflowTypes.ResearchAgent)]
    public async Task Engine_SelectsWorkflow_AndUserIsolationIsPreserved(string? engine, string type)
    {
        await using var db = RefactorTestDbContext.Create();
        var service = Service(db);
        var user = Guid.NewGuid();
        var result = await service.CreateResearchInvestigationAsync(user, new("2330", "營收", Engine: engine));
        Assert.Equal(type, result.AgentRun.WorkflowType);
        Assert.NotNull(await service.GetByIdAsync(result.AgentRun.Id, user));
        Assert.Null(await service.GetByIdAsync(result.AgentRun.Id, Guid.NewGuid()));
    }

    [Theory]
    [InlineData(false, false, "Answered", 1)]
    [InlineData(true, false, "Answered", 2)]
    [InlineData(true, true, "CitationValidationFailed", 2)]
    public async Task FixedWorkflow_AcceptsOrRevisesOnce_WithoutCallingPlanner(bool invalidFirst, bool invalidRevision, string status, int criticCalls)
    {
        await using var db = RefactorTestDbContext.Create();
        var request = new ResearchAskRequest("2330", "營收", SourcePolicy: SourcePolicy.LocalOnly, Engine: "AgentLoop");
        var created = await Service(db).CreateResearchInvestigationAsync(Guid.NewGuid(), request);
        var run = await db.AgentRuns.Include(x => x.Nodes).SingleAsync();
        var chat = new ScriptedChat(invalidFirst, invalidRevision);
        var critic = new CountingCritic();
        var handlers = new IAgentNodeHandler[]
        {
            new NoOpValidation(), new DetectResearchIntentNodeHandler(new IntentDetector()),
            new ResearchAgentLoopNodeHandler(new ResearchAgentLoop(chat, new EvidenceTools(), Options.Create(new ResearchAgentLoopOptions()), TimeProvider.System)),
            new ResearchOutputGateNodeHandler(), new BuildEvidencePacketNodeHandler(), new CheckEvidenceNodeHandler(),
            new CritiqueAnswerNodeHandler(critic), new FinalizeCriticReportNodeHandler([new ResearchAgentPolicyEvaluator()]),
            new RouteResearchAgentQualityNodeHandler(), new FinalizeResearchAgentNodeHandler()
        };
        var executor = AgentExecutorTestFactory.Create(db, new WorkflowGraphTopologyService(), new AgentRunGraphValidator(),
            new AgentRunStateMachine(), new AgentNodeStateMachine(), handlers, NullLogger<AgentRunExecutor>.Instance,
            catalog: new AgentWorkflowCatalog(), dynamicPlanner: new ForbiddenPlanner());
        for (var batch = 0; batch < 10 && run.Status is "Pending" or "Running"; batch++)
            await executor.ExecuteAsync(run.Id, run.UserId);
        Assert.Equal(AgentRunStatuses.Succeeded, run.Status);
        var artifact = await db.ResearchRuns.SingleAsync(x => x.Id == created.ResearchRunId);
        Assert.Equal(status, artifact.Status);
        Assert.Equal(criticCalls, critic.Calls);
        Assert.Equal(invalidFirst ? "Succeeded" : "Skipped", run.Nodes.Single(x => x.NodeKey == "reviseAgent").Status);
        if (invalidRevision) Assert.Equal(ResearchAgentLoop.Refusal, artifact.Answer);
        else Assert.Contains("[1]", artifact.Answer);
        Assert.Equal(3, await db.ResearchRunCandidates.CountAsync());
        var detail = await Service(db).GetByIdAsync(run.Id, run.UserId);
        Assert.NotEmpty(detail!.LlmTurns!);
        Assert.Contains(detail.ToolCalls, x => x.AgentLlmTurnId.HasValue);
    }

    [Fact]
    public void Definition_HasCompleteStaticGraphAndRegisteredContracts()
    {
        var run = new ResearchAgentWorkflowDefinitionProvider().CreateRun(Guid.NewGuid(), Guid.NewGuid(), new("2330", "營收"));
        var order = new WorkflowGraphTopologyService().GetExecutionOrder(run.WorkflowDefinitionJson);
        new AgentRunGraphValidator().Validate(run, order);
        Assert.Equal(ResearchAgentWorkflowDefinitionProvider.Steps.Select(x => x.Key), order);
        Assert.Equal("Static", JsonNode.Parse(run.WorkflowDefinitionJson)!["orchestrationMode"]!.GetValue<string>());
        var catalog = new AgentWorkflowCatalog();
        Assert.All(run.Nodes, node => Assert.NotNull(catalog.GetNode(node.NodeType)));
    }

    [Theory]
    [InlineData("answer [9]", "LocalDocument", "DanglingCitation")]
    [InlineData("answer [1]", "Web", "SourcePolicyViolation")]
    [InlineData("answer [E99]", "LocalDocument", "DanglingEvidenceReference")]
    public void OutputGate_BlocksInvalidEvidence(string answer, string source, string issue)
    {
        var board = new JsonObject { ["answer"] = answer,
            ["researchRequest"] = JsonSerializer.SerializeToNode(new ResearchAskRequest("2330", "營收", SourcePolicy: SourcePolicy.LocalOnly), AgentNodeJson.SerializerOptions),
            ["citations"] = new JsonArray(new JsonObject { ["citationIndex"] = 1, ["sourceType"] = source }) };
        var result = ResearchOutputGateNodeHandler.Check(board);
        Assert.False(result.Passed);
        Assert.Contains(issue, result.BlockingIssues);
    }

    [Theory]
    [InlineData("營收２.８９兆元[1]", "營收 28,943 億元", false)]
    [InlineData("EPS -3.2 元[1]", "EPS 3.2 元", true)]
    [InlineData("營收 999 元[1]", "營收 100 元", true)]
    [InlineData("2024 年營收 100 元[1]", "2024 年營收 100 元", false)]
    [InlineData("營收 100 元", "營收 100 元", true)]
    [InlineData("EPS 66.26 元。[1]", "基本每股盈餘（元） 66.26", false)]
    [InlineData("營收增加[1]", "營收增加", false)]
    public void NumberGate_ChecksCitedQuote_WithNormalizationAndUnits(string answer, string quote, bool blocking)
    {
        var result = ResearchNumberTraceability.Check(answer,
            [new JsonObject { ["citationIndex"] = 1, ["quoteText"] = quote }]);
        Assert.Equal(blocking, result.Blocking);
    }

    [Fact]
    public async Task Retry_PreservesOriginalRequest_AndStartsNewCheckpointAttempt()
    {
        await using var db = RefactorTestDbContext.Create();
        var service = Service(db);
        var user = Guid.NewGuid();
        var created = await service.CreateResearchInvestigationAsync(user, new("2330", "營收", Engine: "AgentLoop"));
        var run = await db.AgentRuns.Include(x => x.Nodes).SingleAsync();
        run.Status = AgentRunStatuses.Failed;
        foreach (var node in run.Nodes) node.Status = AgentNodeStatuses.Skipped;
        await db.SaveChangesAsync();
        await service.RetryAsync(run.Id, user);
        Assert.Equal(1, run.ExecutionAttempt);
        Assert.Equal("2330", JsonNode.Parse(run.BlackboardJson)!["researchRequest"]!["ticker"]!.GetValue<string>());
        Assert.All(run.Nodes, x => Assert.Equal("Pending", x.Status));
    }

    private static AgentRunService Service(EquityLensDbContext db) => new(db,
        [new ResearchAgentWorkflowDefinitionProvider(), new ResearchInvestigationWorkflowDefinitionProvider()],
        new AgentRunStateMachine(), new AgentNodeStateMachine(), new Queue());
    private sealed class Queue : IAgentRunQueue
    {
        public Task EnqueueAsync(AgentRunQueueMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<AgentRunQueueItem?> ReadNextAsync(string consumerName, CancellationToken cancellationToken = default) => Task.FromResult<AgentRunQueueItem?>(null);
        public Task<AgentRunQueueItem?> ReadStalePendingAsync(string consumerName, TimeSpan minIdleTime, CancellationToken cancellationToken = default) => Task.FromResult<AgentRunQueueItem?>(null);
        public Task AcknowledgeAsync(string streamId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class NoOpValidation : IAgentNodeHandler
    {
        public string NodeType => ResearchInvestigationNodeTypes.Validate;
        public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class ForbiddenPlanner : IAgentWorkflowPlanner
    {
        public Task<DynamicPlanProposal> PlanAsync(WorkflowPlanningContext context, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Static workflow must never call planner");
    }
    private sealed class CountingCritic : ICriticReviewAgent
    {
        public int Calls;
        public Task<CriticReviewResult> CritiqueAsync(CriticReviewInput input, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(new CriticReviewResult("已檢查", "None", [], null)); }
    }
    private sealed class ScriptedChat(bool invalidFirst, bool invalidRevision) : IChatCompletionService
    {
        public string Provider => "test"; public string Model => "fixture";
        private int _calls;
        public Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            _calls++;
            var result = _calls == 1 ? """{"action":"call_tools","toolCalls":[{"callId":"c1","tool":"search_local","arguments":{"query":"營收"}}]}"""
                : AgentNodeJson.Serialize(new { action = "submit_answer", answer = new { markdown = "營收 100 元 " + ((_calls == 2 ? invalidFirst : invalidRevision) ? "[E999]" : "[E1]"), citedEvidenceIds = new[] { "E1" } } });
            return Task.FromResult(new ChatCompletionResult(result, Model, 10, 10));
        }
    }
    private sealed class EvidenceTools : IResearchLoopTools
    {
        public IReadOnlyList<LoopToolDefinition> Visible(LoopToolScope scope) => [new("search_local", "test", new())];
        public string? Validate(LoopToolScope scope, LoopToolCall call) => null;
        public Task<LoopToolResult> InvokeAsync(LoopToolScope scope, LoopToolCall call, CancellationToken ct) => Task.FromResult(new LoopToolResult(
            Enumerable.Range(0, 3).Select(i => new RetrievedDocumentChunk(new(Guid.NewGuid(), Guid.NewGuid(), "年報", "AnnualReport", null, i, i + 1,
                null, "營收 100 元", 0, 1, null, "2330", null, null), "Primary", "test", "營收")).ToList(), "3 items"));
    }
}
