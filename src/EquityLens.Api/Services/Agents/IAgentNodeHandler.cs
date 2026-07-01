namespace EquityLens.Api.Services.Agents;

public interface IAgentNodeHandler
{
    string NodeType { get; }

    Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default);
}
