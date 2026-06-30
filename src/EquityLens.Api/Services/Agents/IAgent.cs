using System.Text.Json;

namespace EquityLens.Api.Services.Agents;

/// <summary>
/// Agent 抽象介面。每個 Agent 封裝特定領域的能力，透過 Tool 暴露給 Supervisor 調度。
/// </summary>
public interface IAgent
{
    string Name { get; }
    string Description { get; }
    IReadOnlyList<AgentToolDefinition> GetTools();
    Task<AgentResponse> ExecuteToolAsync(
        string toolName,
        Dictionary<string, JsonElement> args,
        CancellationToken cancellationToken = default);
}
