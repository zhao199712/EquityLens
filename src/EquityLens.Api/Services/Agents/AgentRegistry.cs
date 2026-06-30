namespace EquityLens.Api.Services.Agents;

/// <summary>
/// Agent 註冊表。收集所有已註冊的 IAgent，提供 tool definitions 給 Supervisor。
/// Hub-and-Spoke 架構中的 Hub 使用此表來發現可用的 Agent。
/// </summary>
public sealed class AgentRegistry
{
    private readonly IReadOnlyList<IAgent> _agents;

    public AgentRegistry(IEnumerable<IAgent> agents)
    {
        _agents = agents.ToList();
    }

    public IReadOnlyList<IAgent> Agents => _agents;

    public IReadOnlyList<AgentToolDefinition> GetAllToolDefinitions()
    {
        return _agents
            .SelectMany(agent => agent.GetTools())
            .ToList();
    }

    public IAgent? FindAgentByTool(string toolName)
    {
        return _agents.FirstOrDefault(agent =>
            agent.GetTools().Any(tool => tool.Name == toolName));
    }
}
