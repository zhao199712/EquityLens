namespace EquityLens.Api.Services.Agents;

/// <summary>
/// Planner 呼叫結果。<see cref="TimedOut"/> 為 true 時 <see cref="Proposal"/> 為 null，
/// 呼叫端應改用確定性計畫。
/// </summary>
public sealed record PlannerInvocationResult(DynamicPlanProposal? Proposal, bool TimedOut);

/// <summary>
/// planner tool call 應記錄的狀態。<see cref="UsedFallback"/> 為 true 表示 LLM 沒有產生計畫，
/// 改用確定性計畫繼續執行。
/// </summary>
public sealed record PlannerToolCallOutcome(string Status, string? ErrorMessage, bool UsedFallback);

/// <summary>
/// 以協調器層級的期限呼叫 workflow planner。
/// </summary>
/// <remarks>
/// 這是安全網，不是主要的逾時機制：<see cref="LlmAgentWorkflowPlanner"/> 內部已經有自己的
/// <c>CancelAfter</c>。協調器期限應設得比 planner 自身的逾時稍長，只在 planner 實作
/// 忽略取消權杖或在回傳 Task 前同步阻塞時才會觸發。
/// 等待使用 <see cref="Task.WaitAsync(TimeSpan, CancellationToken)"/>，等待本身不佔用執行緒。
/// </remarks>
public static class PlannerInvocation
{
    /// <summary><see cref="DeterministicDynamicWorkflowPlanner"/> 產生的計畫所使用的 Mode。</summary>
    public const string DeterministicFallbackMode = "DeterministicFallback";

    /// <summary>
    /// 依 planner 回傳的計畫判斷 tool call 狀態。
    /// </summary>
    /// <remarks>
    /// <see cref="LlmAgentWorkflowPlanner"/> 在自身逾時或輸出無法解析時不會丟出例外，而是回傳確定性計畫；
    /// 這種情況 LLM 規劃實際上失敗了，必須記為 Failed 並保留原因，不能記成 Succeeded。
    /// </remarks>
    public static PlannerToolCallOutcome ClassifyToolCall(DynamicPlanProposal proposal) =>
        string.Equals(proposal.Mode, DeterministicFallbackMode, StringComparison.Ordinal)
            ? new PlannerToolCallOutcome(
                AgentToolCallStatuses.Failed,
                proposal.FallbackReason ?? "Workflow planner returned a deterministic fallback plan.",
                UsedFallback: true)
            : new PlannerToolCallOutcome(AgentToolCallStatuses.Succeeded, null, UsedFallback: false);

    public static async Task<PlannerInvocationResult> InvokeAsync(
        IAgentWorkflowPlanner planner,
        WorkflowPlanningContext context,
        TimeSpan deadline,
        CancellationToken cancellationToken)
    {
        var plannerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Task.Run 讓「回傳 Task 之前就同步阻塞」的 planner 也受期限約束：它仍會佔用一條 thread pool
        // 執行緒直到 planner 回傳，但協調流程不會被卡住，期限一到就改用確定性計畫。
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
