using EquityLens.Api.Services.Ai;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Services.Agents.Loop;

/// <summary>研究領域入口；持久化、預算及 Guard 由共用迴圈負責。</summary>
public sealed class ResearchAgentLoop(IChatCompletionService chat, IResearchLoopTools tools,
    IOptions<ResearchAgentLoopOptions> options, TimeProvider clock)
{
    public const string Refusal = AgentLoopEngine.Refusal;
    public Task<ResearchLoopState> RunAsync(AgentNodeExecutionContext context, string mode, CancellationToken ct) =>
        new AgentLoopEngine(chat, tools, options, clock).RunAsync(context, mode, ct);
}
