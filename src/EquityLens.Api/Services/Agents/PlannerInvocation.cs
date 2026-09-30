namespace EquityLens.Api.Services.Agents;

/// <summary>
/// Planner 呼叫結果。<see cref="TimedOut"/> 為 true 時 <see cref="Proposal"/> 為 null，
/// 呼叫端應改用確定性計畫。
/// </summary>
public sealed record PlannerInvocationResult(DynamicPlanProposal? Proposal, bool TimedOut);

/// <summary>
/// 以協調器層級的期限呼叫 workflow planner。
/// </summary>
/// <remarks>
/// 這是安全網，不是主要的逾時機制：<see cref="LlmAgentWorkflowPlanner"/> 內部已經有自己的
/// <c>CancelAfter</c>。協調器期限應設得比 planner 自身的逾時稍長，只在 planner 實作
/// 忽略取消權杖或在回傳 Task 前同步阻塞時才會觸發。
/// 等待使用 <see cref="Task.WaitAsync(TimeSpan, CancellationToken)"/>，不會佔用執行緒。
/// </remarks>
public static class PlannerInvocation
{
    public static async Task<PlannerInvocationResult> InvokeAsync(
        IAgentWorkflowPlanner planner,
        WorkflowPlanningContext context,
        TimeSpan deadline,
        CancellationToken cancellationToken)
    {
        var plannerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Task.Run 讓「回傳 Task 之前就同步阻塞」的 planner 也受期限約束。
        var planning = Task.Run(() => planner.PlanAsync(context, plannerCancellation.Token), plannerCancellation.Token);

        // planner 可能在期限後仍在執行：等它真正結束才釋放權杖來源，並觀察它的例外，
        // 避免 ObjectDisposedException 與 UnobservedTaskException。
        _ = planning.ContinueWith(
            static (task, state) =>
            {
                _ = task.Exception;
                ((CancellationTokenSource)state!).Dispose();
            },
            plannerCancellation,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        try
        {
            var proposal = await planning.WaitAsync(deadline, cancellationToken).ConfigureAwait(false);
            return new PlannerInvocationResult(proposal, TimedOut: false);
        }
        catch (TimeoutException)
        {
            // 通知仍在執行的 planner 停止；若它恰好在這一刻完成，權杖來源可能已被釋放。
            try { plannerCancellation.Cancel(); }
            catch (ObjectDisposedException) { }
            return new PlannerInvocationResult(null, TimedOut: true);
        }
    }
}
