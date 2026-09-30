using System.Diagnostics;
using System.Text.Json.Nodes;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.Ai;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class PlannerInvocationTests
{
    private static readonly WorkflowPlanningContext Context = new(
        Guid.NewGuid(), 1, DynamicPlanningTriggers.ResearchContextReady, new JsonObject(), [], [], [], 0, 0);

    private static DynamicPlanProposal Proposal(WorkflowPlanningContext context) => new(
        Guid.NewGuid(), context.OrchestrationVersion, context.Trigger, DynamicGoalStatuses.Complete, "test", [], [], "Test");

    [Fact]
    public async Task ReturnsProposal_WhenPlannerFinishesBeforeDeadline()
    {
        var planner = new DelegatePlanner((context, _) => Task.FromResult(Proposal(context)));

        var result = await PlannerInvocation.InvokeAsync(planner, Context, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.False(result.TimedOut);
        Assert.NotNull(result.Proposal);
        Assert.Equal("test", result.Proposal!.Reason);
    }

    [Fact]
    public async Task TimesOutAndSignalsCancellation_WhenPlannerIgnoresToken()
    {
        CancellationToken observed = default;
        var neverCompletes = new TaskCompletionSource<DynamicPlanProposal>();
        var planner = new DelegatePlanner((_, token) => { observed = token; return neverCompletes.Task; });

        var result = await PlannerInvocation.InvokeAsync(planner, Context, TimeSpan.FromMilliseconds(100), CancellationToken.None);

        Assert.True(result.TimedOut);
        Assert.Null(result.Proposal);
        Assert.True(observed.IsCancellationRequested, "the still-running planner should be told to stop");
    }

    [Fact]
    public async Task TimesOutWithoutWaitingForPlannerThatBlocksSynchronously()
    {
        using var release = new ManualResetEventSlim(false);
        var planner = new DelegatePlanner((context, _) =>
        {
            release.Wait(TimeSpan.FromSeconds(10)); // blocks before returning a Task
            return Task.FromResult(Proposal(context));
        });
        var stopwatch = Stopwatch.StartNew();

        var result = await PlannerInvocation.InvokeAsync(planner, Context, TimeSpan.FromMilliseconds(100), CancellationToken.None);

        stopwatch.Stop();
        release.Set();
        Assert.True(result.TimedOut);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"deadline was not enforced: {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task PropagatesPlannerException()
    {
        var planner = new DelegatePlanner((_, _) => Task.FromException<DynamicPlanProposal>(new InvalidOperationException("boom")));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PlannerInvocation.InvokeAsync(planner, Context, TimeSpan.FromSeconds(5), CancellationToken.None));

        Assert.Equal("boom", error.Message);
    }

    [Fact]
    public async Task PropagatesCallerCancellation_InsteadOfReportingTimeout()
    {
        using var caller = new CancellationTokenSource();
        var planner = new DelegatePlanner(async (context, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return Proposal(context);
        });
        caller.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PlannerInvocation.InvokeAsync(planner, Context, TimeSpan.FromSeconds(10), caller.Token));
    }

    [Fact]
    public void ClassifyToolCall_MarksDeterministicFallbackAsFailedWithReason()
    {
        var fallback = DeterministicDynamicWorkflowPlanner.Create(ResearchContext(), "Workflow planner timed out after 15 seconds.");

        var outcome = PlannerInvocation.ClassifyToolCall(fallback);

        Assert.True(outcome.UsedFallback);
        Assert.Equal(AgentToolCallStatuses.Failed, outcome.Status);
        Assert.Equal("Workflow planner timed out after 15 seconds.", outcome.ErrorMessage);
    }

    [Fact]
    public void ClassifyToolCall_MarksLlmPlanAsSucceeded()
    {
        var outcome = PlannerInvocation.ClassifyToolCall(Proposal(Context) with { Mode = "Llm" });

        Assert.False(outcome.UsedFallback);
        Assert.Equal(AgentToolCallStatuses.Succeeded, outcome.Status);
        Assert.Null(outcome.ErrorMessage);
    }

    [Fact]
    public async Task LlmPlannerOwnTimeout_IsRecordedAsFailedToolCall_NotSucceeded()
    {
        // The common timeout path: LlmAgentWorkflowPlanner catches its own 15s timeout and returns a
        // deterministic plan instead of throwing, so the orchestrator deadline never fires.
        var planner = new LlmAgentWorkflowPlanner(new TimingOutChat());

        var result = await PlannerInvocation.InvokeAsync(planner, ResearchContext(), TimeSpan.FromSeconds(5), CancellationToken.None);
        var outcome = PlannerInvocation.ClassifyToolCall(result.Proposal!);

        Assert.False(result.TimedOut);
        Assert.Equal(PlannerInvocation.DeterministicFallbackMode, result.Proposal!.Mode);
        Assert.Equal(AgentToolCallStatuses.Failed, outcome.Status);
        Assert.Contains("timed out", outcome.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static WorkflowPlanningContext ResearchContext()
    {
        var run = new ResearchInvestigationWorkflowDefinitionProvider().CreateRun(
            Guid.NewGuid(), Guid.NewGuid(), new("2330", "台積電 2025 年毛利率是多少？"));
        return new WorkflowPlanningContext(
            run.Id,
            run.OrchestrationVersion,
            DynamicPlanningTriggers.ResearchContextReady,
            AgentNodeJson.ParseBlackboard(run.BlackboardJson),
            [],
            new WorkflowSkillCatalog().Skills,
            new NodeCapabilityRegistry().Capabilities,
            0,
            0);
    }

    private sealed class TimingOutChat : IChatCompletionService
    {
        public string Provider => "Test";

        public string Model => "test-model";

        public Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromException<ChatCompletionResult>(new OperationCanceledException("simulated provider timeout"));
    }

    private sealed class DelegatePlanner(Func<WorkflowPlanningContext, CancellationToken, Task<DynamicPlanProposal>> plan)
        : IAgentWorkflowPlanner
    {
        public Task<DynamicPlanProposal> PlanAsync(WorkflowPlanningContext context, CancellationToken cancellationToken = default) =>
            plan(context, cancellationToken);
    }
}
