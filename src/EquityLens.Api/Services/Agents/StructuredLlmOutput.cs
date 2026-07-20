using System.Text.Json;
using EquityLens.Api.Services.Ai;

namespace EquityLens.Api.Services.Agents;

public sealed record StructuredLlmAttempt(int Attempt, string Mode, string Model, int PromptTokens, int CompletionTokens, string OutputPreview, string? ValidationError);

internal static class StructuredLlmOutput
{
    public static async Task<(T? Value, IReadOnlyList<StructuredLlmAttempt> Attempts, string? LastError)> ExecuteAsync<T>(IChatCompletionService chat, string systemPrompt, Func<int, string?, string?, string> buildUserPrompt, Func<string, T> parseAndValidate, int maxTokens, CancellationToken cancellationToken)
    {
        var attempts = new List<StructuredLlmAttempt>(); string? previousOutput = null; string? validationError = null;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var response = await chat.CompleteAsync(new ChatCompletionRequest(systemPrompt, buildUserPrompt(attempt, previousOutput, validationError), .1, maxTokens, ChatResponseFormat.JsonObject), cancellationToken);
            previousOutput = response.Content;
            try
            {
                if (string.IsNullOrWhiteSpace(response.Content)) throw new InvalidOperationException("Model returned empty content.");
                var value = parseAndValidate(response.Content);
                attempts.Add(new(attempt, attempt == 1 ? "Llm" : "LlmRepair", response.Model, response.PromptTokens, response.CompletionTokens, AgentNodeJson.Trim(response.Content, 500), null));
                return (value, attempts, null);
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                validationError = exception.Message;
                attempts.Add(new(attempt, attempt == 1 ? "Llm" : "LlmRepair", response.Model, response.PromptTokens, response.CompletionTokens, AgentNodeJson.Trim(response.Content ?? string.Empty, 500), validationError));
            }
        }
        return (default, attempts, validationError);
    }
}
