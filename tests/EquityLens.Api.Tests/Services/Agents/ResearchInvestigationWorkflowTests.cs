using System.Text.Json;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.Ai;
using EquityLens.Api.Services.Ai.Retrieval;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class ResearchInvestigationWorkflowTests
{
    [Fact]
    public void CreateRun_BuildsBootstrapGraphAndLinksArtifact()
    {
        var provider = new ResearchInvestigationWorkflowDefinitionProvider();
        var userId = Guid.NewGuid();
        var researchRunId = Guid.NewGuid();

        var run = provider.CreateRun(userId, researchRunId, new ResearchAskRequest("2330", "主要風險是什麼？"));

        Assert.Equal(AgentWorkflowTypes.ResearchInvestigation, run.WorkflowType);
        Assert.Equal(researchRunId, run.ResearchRunId);
        Assert.Equal(2, run.Nodes.Count);
        Assert.Equal(ResearchInvestigationNodeTypes.Validate, run.Nodes.First().NodeType);
        Assert.Equal(ResearchInvestigationNodeTypes.DetectIntent, run.Nodes.Last().NodeType);

        using var definition = JsonDocument.Parse(run.WorkflowDefinitionJson);
        Assert.Equal("DynamicStateful", definition.RootElement.GetProperty("orchestrationMode").GetString());
        Assert.Equal(2, definition.RootElement.GetProperty("nodes").GetArrayLength());
        Assert.Single(definition.RootElement.GetProperty("edges").EnumerateArray());
    }

    [Theory]
    [InlineData(0, "LocalDocument")]
    [InlineData(1, "Web")]
    public async Task BuildEvidencePacket_AcceptsLegacyNumericCitationSourceType(int sourceType, string expected)
    {
        await using var db = CreateDbContext();
        var run = new AgentRun
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            WorkflowType = AgentWorkflowTypes.ResearchInvestigation,
            AgentType = AgentTypes.Research,
            Status = AgentRunStatuses.Running,
            BlackboardJson = $$"""
                {
                  "answer": "answer [1]",
                  "researchRun": {
                    "run": { "ticker": "2330", "question": "question", "status": "Answered" },
                    "citations": [{ "citationIndex": 1, "sourceType": {{sourceType}}, "title": "source", "quoteText": "quote" }],
                    "candidates": [{ "decision": "Selected" }]
                  }
                }
                """
        };
        var node = new AgentRunNode { Id = Guid.NewGuid(), AgentRunId = run.Id, NodeKey = ResearchQualityReviewNodeKeys.BuildEvidencePacket, NodeType = ResearchQualityReviewNodeTypes.BuildEvidencePacket, Status = AgentNodeStatuses.Running };

        await new BuildEvidencePacketNodeHandler().ExecuteAsync(new AgentNodeExecutionContext(db, run, node, (_, _, _, _, _) => { }));

        using var board = JsonDocument.Parse(run.BlackboardJson);
        Assert.Equal(expected, board.RootElement.GetProperty(AgentBlackboardKeys.EvidencePacket).GetProperty("citations")[0].GetProperty("sourceType").GetString());
    }

    [Fact]
    public async Task RetryResearchInvestigation_RestoresOriginalRequestAndResearchRunLink()
    {
        await using var db = CreateDbContext();
        var provider = new ResearchInvestigationWorkflowDefinitionProvider();
        var request = new ResearchAskRequest("2330", "主要風險是什麼？", SourcePolicy: SourcePolicy.LocalThenWeb, TopK: 7, Temperature: 0.3);
        var artifact = new ResearchRun { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), TraceId = "trace", Ticker = "2330", Question = request.Question, Answer = "old", Status = "Failed" };
        var run = provider.CreateRun(artifact.UserId, artifact.Id, request);
        run.Status = AgentRunStatuses.Failed;
        run.ErrorMessage = "failed";
        run.Nodes.First().Status = AgentNodeStatuses.Failed;
        db.ResearchRuns.Add(artifact);
        db.AgentRuns.Add(run);
        await db.SaveChangesAsync();
        var queue = new RecordingQueue();
        var service = new AgentRunService(db, [provider], new AgentRunStateMachine(), new AgentNodeStateMachine(), queue);

        var retried = await service.RetryAsync(run.Id, artifact.UserId);

        Assert.NotNull(retried);
        Assert.Equal(AgentRunStatuses.Pending, retried.Status);
        Assert.Equal("Pending", artifact.Status);
        Assert.Null(artifact.ErrorMessage);
        Assert.Equal(run.Id, queue.RunId);
        using var board = JsonDocument.Parse(run.BlackboardJson);
        var restored = board.RootElement.GetProperty(AgentBlackboardKeys.ResearchRequest);
        Assert.Equal("2330", restored.GetProperty("ticker").GetString());
        Assert.Equal(request.Question, restored.GetProperty("question").GetString());
        Assert.Equal(7, restored.GetProperty("topK").GetInt32());
        Assert.Equal(artifact.Id, board.RootElement.GetProperty(AgentBlackboardKeys.ResearchRunId).GetGuid());
    }

    [Fact]
    public async Task RankEvidence_RerankTimeout_PersistsFailedToolCallAndCompletesWithFallback()
    {
        await using var db = CreateDbContext();
        var run = new ResearchInvestigationWorkflowDefinitionProvider().CreateRun(
            Guid.NewGuid(), Guid.NewGuid(), new ResearchAskRequest("2454", "成長與風險", TopK: 5));
        run.Status = AgentRunStatuses.Running;
        var node = new AgentRunNode
        {
            Id = Guid.NewGuid(), AgentRunId = run.Id, NodeKey = "rank:1",
            NodeType = ResearchInvestigationNodeTypes.RankEvidence, Status = AgentNodeStatuses.Running
        };
        run.Nodes.Add(node);
        var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        board[AgentBlackboardKeys.InitialEvidence] = JsonSerializer.SerializeToNode(Array.Empty<RetrievedDocumentChunk>(), AgentNodeJson.SerializerOptions);
        board[AgentBlackboardKeys.ResearchIntent] = JsonSerializer.SerializeToNode(new IntentDetectionResult(ResearchQuestionIntent.General, [], 1), AgentNodeJson.SerializerOptions);
        run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);
        db.AgentRuns.Add(run);
        await db.SaveChangesAsync();
        var events = new List<string>();
        var handler = new RankAndSelectResearchEvidenceNodeHandler(
            new TimeoutFallbackReranker(),
            Options.Create(new RetrievalOptions { RerankProvider = "Cohere", DefaultTopK = 10, MaxTopK = 20 }));

        await handler.ExecuteAsync(new AgentNodeExecutionContext(db, run, node, (_, _, eventType, _, _) => events.Add(eventType)));
        await db.SaveChangesAsync();

        var toolCall = await db.AgentToolCalls.SingleAsync();
        Assert.Equal("cohereRerank", toolCall.ToolName);
        Assert.Equal(AgentToolCallStatuses.Failed, toolCall.Status);
        Assert.Equal("CohereTimeout", toolCall.ErrorMessage);
        Assert.Contains("TimedOut", toolCall.ResultJson);
        Assert.Contains("local ranking fallback", toolCall.ResultPreview);
        Assert.Contains(AgentEventTypes.ToolCallFailed, events);
        using var output = JsonDocument.Parse(node.OutputJson!);
        Assert.True(output.RootElement.GetProperty("rerankDiagnostics").GetProperty("usedFallback").GetBoolean());
    }

    private static EquityLensDbContext CreateDbContext() => new TestDbContext(
        new DbContextOptionsBuilder<EquityLensDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class TestDbContext(DbContextOptions<EquityLensDbContext> options) : EquityLensDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Ignore<DocumentEmbedding>();
            modelBuilder.Entity<AgentRun>().Property(x => x.Id).ValueGeneratedNever();
            modelBuilder.Entity<AgentRunNode>().Property(x => x.Id).ValueGeneratedNever();
        }
    }

    private sealed class RecordingQueue : IAgentRunQueue
    {
        public Guid? RunId { get; private set; }
        public Task EnqueueAsync(AgentRunQueueMessage message, CancellationToken cancellationToken = default)
        {
            RunId = message.RunId;
            return Task.CompletedTask;
        }
        public Task<AgentRunQueueItem?> ReadNextAsync(string consumerName, CancellationToken cancellationToken = default) => Task.FromResult<AgentRunQueueItem?>(null);
        public Task<AgentRunQueueItem?> ReadStalePendingAsync(string consumerName, TimeSpan minIdleTime, CancellationToken cancellationToken = default) => Task.FromResult<AgentRunQueueItem?>(null);
        public Task AcknowledgeAsync(string streamId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TimeoutFallbackReranker : IResultReranker
    {
        public Task<RankedSelection> Rank(
            IReadOnlyList<RetrievedDocumentChunk> chunks,
            ResearchQuestionIntent intent,
            int topK,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new RankedSelection([], [], new(
                "Cohere", "TimedOut", "rerank-v4.0-fast", true, "CohereTimeout",
                10_001, chunks.Count, 0, 70_822, null)));
    }
}
