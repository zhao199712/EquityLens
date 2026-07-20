namespace EquityLens.Api.Data.Entities;

public sealed class AgentRunWakeOutbox
{
    public Guid Id { get; set; }
    public Guid AgentRunId { get; set; }
    public Guid UserId { get; set; }
    public string WorkflowType { get; set; } = string.Empty;
    public Guid? AgentRunNodeId { get; set; }
    public int DefinitionVersion { get; set; }
    public long OrchestrationVersion { get; set; }
    public Guid CorrelationId { get; set; }
    public Guid? CausationId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAtUtc { get; set; }
}
