using System.Text;
using System.Text.Json.Nodes;

namespace EquityLens.Api.Services.Ai;

public enum ChatResponseFormat
{
    Text,
    JsonObject
}

public sealed record ChatCompletionRequest(
    string SystemPrompt,
    string UserPrompt,
    double Temperature = 0.2,
    int MaxTokens = 4096,
    ChatResponseFormat ResponseFormat = ChatResponseFormat.Text,
    ChatToolProtocol? ToolProtocol = null);

public sealed record ChatCompletionResult(
    string Content,
    string Model,
    int PromptTokens,
    int CompletionTokens,
    JsonArray? ProviderOutput = null);

public sealed record ChatFunction(string Name, string Description, JsonObject Parameters);
public sealed record ChatToolProtocol(IReadOnlyList<ChatFunction> Tools, IReadOnlyList<ChatToolExchange> Exchanges);

/// <summary>與決策一起保存的供應商訊息；工具結果僅於執行完成後加入。</summary>
public sealed class ChatToolExchange
{
    public int Turn { get; set; }
    public JsonArray Output { get; set; } = [];
    public JsonArray Results { get; set; } = [];
}

public interface IChatCompletionService
{
    bool SupportsNativeTools => false;
    int EstimateInputBytes(ChatCompletionRequest request) => Encoding.UTF8.GetByteCount(request.SystemPrompt + request.UserPrompt);
    string Provider { get; }
    string Model { get; }
    Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);
}
