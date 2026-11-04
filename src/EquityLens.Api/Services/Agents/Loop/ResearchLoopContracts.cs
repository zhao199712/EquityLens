using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Services.Ai;
using EquityLens.Api.Services.Ai.Retrieval;

namespace EquityLens.Api.Services.Agents.Loop;

public sealed class ResearchAgentLoopOptions
{
    public const string SectionName = "AgentRuntime:ResearchAgentLoop";
    public string DefaultEngine { get; set; } = "AgentLoop";
    public int MaxTurns { get; set; } = 6;
    public int MaxRevisionTurns { get; set; } = 3;
    public int MaxToolCalls { get; set; } = 16;
    public int MaxToolCallsPerTurn { get; set; } = 4;
    public int MaxSkills { get; set; } = 3;
    public int MaxWebCalls { get; set; } = 1;
    public int MaxPromptTokens { get; set; } = 60000;
    public int MaxOutputTokens { get; set; } = 2500;
    public decimal MaxCostUsd { get; set; } = .10m;
    // 保守估價，部署時依供應商費率設定；不綁定特定模型。
    public decimal InputUsdPerMillion { get; set; } = 1m;
    public decimal OutputUsdPerMillion { get; set; } = 4m;
    public decimal ToolCostReserveUsd { get; set; } = .005m;
    public int MaxWallClockSeconds { get; set; } = 240;
    public int ApprovalExpiryHours { get; set; } = 24;
    public int ToolTimeoutSeconds { get; set; } = 30;
}

public sealed record LoopEvidence(string Id, RetrievedDocumentChunk Chunk);
public sealed record LoopToolCall(string CallId, string Tool, JsonObject Arguments);
public sealed record LoopAnswer(string Markdown, IReadOnlyList<string> CitedEvidenceIds);
public sealed record LoopAction(string Action, string ThoughtSummary, IReadOnlyList<LoopToolCall> ToolCalls, LoopAnswer? Answer);
public sealed record LoopToolResult(IReadOnlyList<RetrievedDocumentChunk> Evidence, string Summary, string? Error = null, JsonObject? Data = null, JsonObject? BlackboardPatch = null, JsonObject? Diagnostics = null);
public sealed record LoopToolScope(Guid UserId, ResearchAskRequest Request, IReadOnlyList<LoopEvidence> Ledger, bool LocalSearchCompleted, string? IdempotencyKey = null, Guid? RunId = null, string? InputJson = null, JsonObject? DomainContext = null);
public sealed record LoopToolDefinition(string Name, string Description, JsonObject Parameters, decimal EstimatedCostUsd = 0, bool RequiresApproval = false);

public interface IResearchLoopTools : IAgentLoopTools { }

public interface IAgentLoopTools
{
    IReadOnlyList<LoopToolDefinition> Visible(LoopToolScope scope);
    string? Validate(LoopToolScope scope, LoopToolCall call);
    Task<LoopToolResult> InvokeAsync(LoopToolScope scope, LoopToolCall call, CancellationToken ct);
}

public sealed record LoopDataFact(string Id, string Key, string Tool, JsonObject Data);

public sealed class ResearchLoopState
{
    public List<ChatToolExchange> NativeExchanges { get; set; } = [];
    public int Turn { get; set; }
    public Guid? PendingTurnId { get; set; }
    public List<LoopEvidence> Ledger { get; set; } = [];
    public List<LoopDataFact> Facts { get; set; } = [];
    public List<string> History { get; set; } = [];
    public bool LocalSearchCompleted { get; set; }
    public int ToolCalls { get; set; }
    public int WebCalls { get; set; }
    public int GuardDenials { get; set; }
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public decimal CostUsd { get; set; }
    public int NoProgressTurns { get; set; }
    public int ProtocolErrors { get; set; }
    public int AnswerRepairCount { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public string? StopReason { get; set; }
    public LoopAnswer? Answer { get; set; }
    public List<Guid> ResumedApprovalIds { get; set; } = [];
    public bool ForcedDraft { get; set; }
}
