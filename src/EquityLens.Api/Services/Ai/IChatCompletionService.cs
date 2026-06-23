namespace EquityLens.Api.Services.Ai;

public sealed record ChatCompletionRequest(
    string SystemPrompt,
    string UserPrompt,
    double Temperature = 0.2,
    int MaxTokens = 4096);

public sealed record ChatCompletionResult(
    string Content,
    string Model,
    int PromptTokens,
    int CompletionTokens);

public interface IChatCompletionService
{
    string Provider { get; }
    string Model { get; }
    Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);
}
