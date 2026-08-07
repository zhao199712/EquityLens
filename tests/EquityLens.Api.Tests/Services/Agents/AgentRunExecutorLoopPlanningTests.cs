using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class AgentRunExecutorLoopPlanningTests
{
    [Fact]
    public async Task ExecuteAsync_FirstPlanRejection_RepairsOnceAndMaterializesPlan()
    {
        await using var db = CreateDb();
        var run = CreateCompletedInitialReview();
        db.AgentRuns.Add(run);
        await db.SaveChangesAsync();
        var validator = new RejectThenAcceptValidator();
        var executor = CreateExecutor(db, validator);

        await executor.ExecuteAsync(run.Id, run.UserId);

        await db.Entry(run).ReloadAsync();
        await db.Entry(run).Collection(x => x.Nodes).LoadAsync();
        Assert.Equal(AgentRunStatuses.Running, run.Status);
        Assert.Equal(2, validator.CallCount);
        Assert.Contains(run.Nodes, x => x.TemplateNodeKey is not null);
        Assert.Equal(2, await db.AgentRunEvents.CountAsync(x => x.AgentRunId == run.Id && x.EventType == AgentEventTypes.PlannerProposed));
        Assert.Single(await db.AgentRunEvents.Where(x => x.AgentRunId == run.Id && x.EventType == AgentEventTypes.PlanRejected).ToListAsync());
        Assert.Single(await db.AgentRunEvents.Where(x => x.AgentRunId == run.Id && x.EventType == AgentEventTypes.PlanValidated).ToListAsync());
    }

    [Fact]
    public async Task ExecuteAsync_RepairPlanRejected_MarksLoopAndRunFailed()
    {
        await using var db = CreateDb();
        var run = CreateCompletedInitialReview();
        db.AgentRuns.Add(run);
        await db.SaveChangesAsync();
        var validator = new AlwaysRejectValidator();
        var executor = CreateExecutor(db, validator);

        await executor.ExecuteAsync(run.Id, run.UserId);

        await db.Entry(run).ReloadAsync();
        Assert.Equal(AgentRunStatuses.Failed, run.Status);
        Assert.Equal(2, validator.CallCount);
        var runtime = AgentNodeJson.ParseBlackboard(run.BlackboardJson)[AgentBlackboardKeys.Runtime]!;
        Assert.Equal("Failed", runtime["status"]!.GetValue<string>());
        Assert.Equal(AgentLoopStopReasons.PlanValidationFailed, runtime["stopReason"]!.GetValue<string>());
        Assert.Equal(2, await db.AgentRunEvents.CountAsync(x => x.AgentRunId == run.Id && x.EventType == AgentEventTypes.PlanRejected));
        Assert.Single(await db.AgentRunEvents.Where(x => x.AgentRunId == run.Id && x.EventType == AgentEventTypes.LoopStopped).ToListAsync());
    }

    private static AgentRunExecutor CreateExecutor(EquityLensDbContext db, IDynamicPlanValidator validator) => new(
        db,
        new WorkflowGraphTopologyService(),
        new AgentRunGraphValidator(),
        new AgentRunStateMachine(),
        new AgentNodeStateMachine(),
        Array.Empty<IAgentNodeHandler>(),
        NullLogger<AgentRunExecutor>.Instance,
        catalog: new AgentWorkflowCatalog(),
        dynamicPlanValidator: validator,
        graphMaterializer: new GraphMaterializer(db, new AgentWorkflowCatalog()),
        loopController: new AgentLoopController([new ResearchQualityReviewLoopPolicy()]));

    private static AgentRun CreateCompletedInitialReview()
    {
        var run = new ResearchQualityReviewWorkflowDefinitionProvider().CreateRun(Guid.NewGuid(), Guid.NewGuid());
        var completedAt = DateTime.UtcNow;
        foreach (var node in run.Nodes)
        {
            node.Status = AgentNodeStatuses.Succeeded;
            node.CompletedAtUtc = completedAt;
        }
        run.Nodes.Single(x => x.NodeType == ResearchQualityReviewNodeTypes.FinalizeCriticReport).CompletedAtUtc = completedAt.AddMilliseconds(1);
        var question = "台積電今天最新財測為何？";
        var board = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        board[AgentBlackboardKeys.Question] = question;
        board[AgentBlackboardKeys.Answer] = "insufficient";
        board[AgentBlackboardKeys.ResearchRequest] = JsonSerializer.SerializeToNode(
            new ResearchAskRequest("2330", question, SourcePolicy: SourcePolicy.LocalOnly),
            AgentNodeJson.SerializerOptions);
        board[AgentBlackboardKeys.CriticReview] = new JsonObject
        {
            [CriticReviewFields.RequiresRevision] = true,
            [CriticReviewFields.RequiresMoreEvidence] = true
        };
        run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);
        return run;
    }

    private static EquityLensDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<EquityLensDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestEquityLensDbContext(options);
    }

    private sealed class RejectThenAcceptValidator : IDynamicPlanValidator
    {
        public int CallCount { get; private set; }

        public ValidatedDynamicPlan Validate(AgentRun run, DynamicPlanProposal proposal)
        {
            CallCount++;
            if (CallCount == 1) throw new InvalidOperationException("Synthetic first-plan rejection.");
            return new ValidatedDynamicPlan(proposal, proposal.Actions);
        }
    }

    private sealed class AlwaysRejectValidator : IDynamicPlanValidator
    {
        public int CallCount { get; private set; }

        public ValidatedDynamicPlan Validate(AgentRun run, DynamicPlanProposal proposal)
        {
            CallCount++;
            throw new InvalidOperationException("Synthetic plan rejection.");
        }
    }

    private sealed class TestEquityLensDbContext(DbContextOptions<EquityLensDbContext> options) : EquityLensDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Ignore<DocumentEmbedding>();
            modelBuilder.Entity<AgentRun>().Property(x => x.Id).ValueGeneratedNever();
            modelBuilder.Entity<AgentRunNode>().Property(x => x.Id).ValueGeneratedNever();
            modelBuilder.Entity<AgentRunEvent>().Property(x => x.Id).ValueGeneratedNever();
            modelBuilder.Entity<AgentToolCall>().Property(x => x.Id).ValueGeneratedNever();
            modelBuilder.Entity<AgentRunWakeOutbox>().Property(x => x.Id).ValueGeneratedNever();
        }
    }
}
