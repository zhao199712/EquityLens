using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Services.Chat;

public sealed record ChatAgentRequest(
    Guid SessionId,
    string UserMessage,
    IReadOnlyList<ChatMessage> History);

public sealed record ChatAgentResponse(
    string Content,
    IReadOnlyList<ChatToolExecution> ToolExecutions,
    string Model,
    int PromptTokens,
    int CompletionTokens);

public sealed record ChatToolExecution(
    string ToolName,
    string Arguments,
    string ResultPreview);

public interface IChatAgentService
{
    IAsyncEnumerable<ChatAgentStreamEvent> ChatStreamAsync(
        ChatAgentRequest request,
        CancellationToken cancellationToken = default);
}

public abstract record ChatAgentStreamEvent
{
    public sealed record TextDelta(string Delta) : ChatAgentStreamEvent;
    public sealed record ToolCallStart(string ToolName, string Arguments) : ChatAgentStreamEvent;
    public sealed record ToolCallEnd(string ToolName, string ResultPreview) : ChatAgentStreamEvent;
    public sealed record Done(string Model, int PromptTokens, int CompletionTokens) : ChatAgentStreamEvent;
    public sealed record Error(string Message) : ChatAgentStreamEvent;
}
