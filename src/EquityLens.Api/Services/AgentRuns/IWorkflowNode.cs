namespace EquityLens.Api.Services.AgentRuns;

/// <summary>
/// Represents the result of executing a single workflow node.
/// </summary>
public sealed record NodeExecutionResult(
    bool Success,
    string? OutputJson = null,
    string? ErrorMessage = null);

/// <summary>
/// Executes a workflow node within an AgentRun context.
/// </summary>
public interface IWorkflowNode
{
    string NodeType { get; }
    Task<NodeExecutionResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Context passed to each workflow node during execution.
/// </summary>
public sealed class WorkflowNodeContext
{
    public Guid AgentRunId { get; init; }
    public string BlackboardJson { get; set; } = "{}";
    public string? InputJson { get; set; }
    public IServiceProvider ServiceProvider { get; init; } = null!;
}
