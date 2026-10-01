using System.Text.Json.Nodes;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.Ai;
using Microsoft.Extensions.Time.Testing;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class PlannerInvocationTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Deadline = TimeSpan.FromMilliseconds(100);
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
        var clock = new FakeTimeProvider();
        await using var planner = new ControlledPlanner();
        var invocation = PlannerInvocation.InvokeAsync(planner, Context, Deadline, CancellationToken.None, clock);
        var observed = await planner.Started.Task.WaitAsync(Guard);

        clock.Advance(Deadline);
        var result = await invocation.WaitAsync(Guard);

        Assert.True(result.TimedOut);
        Assert.Null(result.Proposal);
        Assert.True(observed.IsCancellationRequested, "the still-running planner should be told to stop");
        Assert.False(planner.Finished.Task.IsCompleted);
        Assert.True(observed.WaitHandle.WaitOne(0)); // CTS must remain usable while the planner is still running
    }

    [Fact]
    public async Task TimesOutWithoutWaitingForPlannerThatBlocksSynchronously()
    {
        var clock = new FakeTimeProvider();
        using var release = new ManualResetEventSlim(false);
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var planner = new DelegatePlanner((context, token) =>
        {
            try
            {
                started.TrySetResult(token);
                release.Wait(); // blocks before returning a Task; finally below always releases it
                return Task.FromResult(Proposal(context));
            }
            finally { finished.TrySetResult(); }
        });
        try
        {
            var invocation = PlannerInvocation.InvokeAsync(planner, Context, Deadline, CancellationToken.None, clock);
            var observed = await started.Task.WaitAsync(Guard);
            clock.Advance(Deadline);
            var result = await invocation.WaitAsync(Guard);

            Assert.True(result.TimedOut);
            Assert.True(observed.IsCancellationRequested);
            Assert.False(finished.Task.IsCompleted);
        }
        finally
        {
            release.Set();
            await finished.Task.WaitAsync(Guard);
        }
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
        await using var planner = new ControlledPlanner(honorCancellation: true);
        var invocation = PlannerInvocation.InvokeAsync(planner, Context, Deadline, caller.Token, new FakeTimeProvider());
        var observed = await planner.Started.Task.WaitAsync(Guard);
        caller.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invocation.WaitAsync(Guard));
        Assert.True(observed.IsCancellationRequested);
        await planner.Finished.Task.WaitAsync(Guard);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateCompletionOrFault_DoesNotChangeTimeoutResult(bool fault)
    {
        var clock = new FakeTimeProvider();
        await using var planner = new ControlledPlanner();
        var invocation = PlannerInvocation.InvokeAsync(planner, Context, Deadline, CancellationToken.None, clock);
        var token = await planner.Started.Task.WaitAsync(Guard);
        clock.Advance(Deadline);
        var result = await invocation.WaitAsync(Guard);

        Assert.True(result.TimedOut);
        if (fault) planner.Release.TrySetException(new InvalidOperationException("late failure"));
        else planner.Release.TrySetResult(Proposal(Context));
        await planner.Finished.Task.WaitAsync(Guard);
        Assert.True((await invocation).TimedOut);
        Assert.True(token.IsCancellationRequested);
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

    private sealed class ControlledPlanner(bool honorCancellation = false) : IAgentWorkflowPlanner, IAsyncDisposable
    {
        public TaskCompletionSource<CancellationToken> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<DynamicPlanProposal> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<DynamicPlanProposal> PlanAsync(WorkflowPlanningContext context, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult(cancellationToken);
            try
            {
                return honorCancellation
                    ? await Release.Task.WaitAsync(cancellationToken)
                    : await Release.Task;
            }
            finally { Finished.TrySetResult(); }
        }

        public async ValueTask DisposeAsync()
        {
            Release.TrySetResult(Proposal(Context));
            await Finished.Task.WaitAsync(Guard);
        }
    }
}
