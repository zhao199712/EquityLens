using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Observability;
using EquityLens.Api.Services.Ai;
using EquityLens.Api.Services.Chat;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Services.Agents;

/// <summary>
/// Hub-and-Spoke 架構的中樞 Orchestrator。
/// 替換 DeepSeekChatAgentService，使用 LLM 動態決定要呼叫哪個 Agent，
/// 透過 AgentRegistry 路由 tool calls 到對應的 Agent。
/// </summary>
public sealed class SupervisorAgent : IChatAgentService
{
    private const int MaxIterations = 10;

    private readonly HttpClient _httpClient;
    private readonly DeepSeekOptions _options;
    private readonly AgentRegistry _agentRegistry;
    private readonly ILogger<SupervisorAgent> _logger;

    public SupervisorAgent(
        HttpClient httpClient,
        IOptions<DeepSeekOptions> options,
        AgentRegistry agentRegistry,
        ILogger<SupervisorAgent> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _agentRegistry = agentRegistry;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
    }

    public async IAsyncEnumerable<ChatAgentStreamEvent> ChatStreamAsync(
        ChatAgentRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var apiKey = string.IsNullOrWhiteSpace(_options.ApiKey)
            ? Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY")
            : _options.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("DeepSeek API key is not configured.");

        var model = string.IsNullOrWhiteSpace(_options.Model) ? "deepseek-chat" : _options.Model;
        var tools = BuildToolDefinitions();
        var messages = BuildMessages(request.History, request.UserMessage);
        var promptTokens = 0;
        var completionTokens = 0;

        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            using var activity = EquityLensTelemetry.ActivitySource.StartActivity("supervisor.llm.request");
            activity?.SetTag("supervisor.provider", "deepseek");
            activity?.SetTag("supervisor.model", model);
            activity?.SetTag("supervisor.iteration", iteration);
            activity?.SetTag("supervisor.session_id", request.SessionId);

            var stopwatch = Stopwatch.StartNew();
            SupervisorLlmResponse? result = null;
            string? apiError = null;
            try
            {
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                httpRequest.Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        model,
                        messages,
                        tools,
                        tool_choice = "auto",
                        temperature = 0.3,
                        max_tokens = 4096
                    }, SerializerOptions),
                    Encoding.UTF8,
                    "application/json");

                using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                stopwatch.Stop();
                EquityLensTelemetry.ChatLlmDuration.Record(stopwatch.Elapsed.TotalMilliseconds,
                    new TagList { { "model", model }, { "status", response.IsSuccessStatusCode ? "success" : "error" } });

                if (!response.IsSuccessStatusCode)
                {
                    activity?.SetTag("supervisor.http_status", (int)response.StatusCode);
                    activity?.SetStatus(ActivityStatusCode.Error, response.StatusCode.ToString());
                    _logger.LogError("Supervisor LLM API error: {StatusCode}", response.StatusCode);
                    apiError = $"Supervisor API error: {response.StatusCode}";
                }
                else
                {
                    result = JsonSerializer.Deserialize<SupervisorLlmResponse>(responseBody, SerializerOptions);
                    activity?.SetStatus(ActivityStatusCode.Ok);
                }
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                EquityLensTelemetry.MarkError(activity, exception);
                throw;
            }

            if (apiError is not null)
            {
                yield return new ChatAgentStreamEvent.Error(apiError);
                yield break;
            }

            var choice = result?.Choices?.FirstOrDefault();
            if (choice?.Message is null)
            {
                yield return new ChatAgentStreamEvent.Error("LLM returned no choices.");
                yield break;
            }

            promptTokens += result?.Usage?.PromptTokens ?? 0;
            completionTokens += result?.Usage?.CompletionTokens ?? 0;
            var toolCalls = choice.Message.ToolCalls ?? [];

            if (toolCalls.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(choice.Message.Content))
                    yield return new ChatAgentStreamEvent.TextDelta(choice.Message.Content);

                yield return new ChatAgentStreamEvent.Done(result?.Model ?? model, promptTokens, completionTokens);
                yield break;
            }

            messages.Add(new
            {
                role = "assistant",
                content = choice.Message.Content,
                tool_calls = toolCalls.Select(call => new
                {
                    id = call.Id,
                    type = call.Type,
                    function = new { name = call.Function.Name, arguments = call.Function.Arguments }
                }).ToArray()
            });

            foreach (var toolCall in toolCalls)
            {
                var arguments = ParseArguments(toolCall.Function.Arguments);
                yield return new ChatAgentStreamEvent.ToolCallStart(toolCall.Function.Name, toolCall.Function.Arguments);

                using var toolActivity = EquityLensTelemetry.ActivitySource.StartActivity("supervisor.tool.execute");
                toolActivity?.SetTag("supervisor.tool.name", toolCall.Function.Name);
                toolActivity?.SetTag("supervisor.session_id", request.SessionId);
                var toolStopwatch = Stopwatch.StartNew();
                var toolResult = await ExecuteToolAsync(toolCall.Function.Name, arguments, cancellationToken);
                toolStopwatch.Stop();
                toolActivity?.SetStatus(ActivityStatusCode.Ok);
                EquityLensTelemetry.ChatToolCalls.Add(1, new TagList { { "tool", toolCall.Function.Name } });
                EquityLensTelemetry.ChatToolDuration.Record(toolStopwatch.Elapsed.TotalMilliseconds,
                    new TagList { { "tool", toolCall.Function.Name } });

                var preview = toolResult.Length > 500 ? toolResult[..500] + "..." : toolResult;
                yield return new ChatAgentStreamEvent.ToolCallEnd(toolCall.Function.Name, preview);
                messages.Add(new { role = "tool", tool_call_id = toolCall.Id, content = toolResult });
            }
        }

        yield return new ChatAgentStreamEvent.Error("Max iterations reached.");
    }

    private async Task<string> ExecuteToolAsync(
        string toolName,
        Dictionary<string, JsonElement> arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            var agent = _agentRegistry.FindAgentByTool(toolName);
            if (agent is null)
                return $"Unknown tool: {toolName}";

            var response = await agent.ExecuteToolAsync(toolName, arguments, cancellationToken);

            var output = new StringBuilder();
            output.AppendLine(response.Content);

            if (response.Citations.Count > 0)
            {
                output.AppendLine();
                output.AppendLine("引用來源：");
                foreach (var citation in response.Citations)
                {
                    output.AppendLine($"[{citation.Index}] {citation.Title} (Page {citation.PageNumber}, {citation.DocumentType})");
                    output.AppendLine($"    {citation.QuoteText}");
                }
            }

            if (response.Trace is not null)
            {
                output.AppendLine();
                output.AppendLine($"[Debug] Model: {response.Model}, Intent: {response.Trace.Intent.Selected}, Strategy: {response.Trace.RetrievalStrategy.Mode}");
            }

            return output.ToString();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Tool execution failed: {ToolName}", toolName);
            return $"Tool execution error: {exception.Message}";
        }
    }

    private object[] BuildToolDefinitions()
    {
        return _agentRegistry.GetAllToolDefinitions()
            .Select(tool => new
            {
                type = "function",
                function = new
                {
                    name = tool.Name,
                    description = tool.Description,
                    parameters = tool.Parameters
                }
            })
            .ToArray();
    }

    private static List<object> BuildMessages(IReadOnlyList<ChatMessage> history, string userMessage)
    {
        var messages = new List<object> { new { role = "system", content = SystemPrompt } };
        foreach (var message in history.Skip(Math.Max(0, history.Count - 20)))
        {
            if ((message.Role is "user" or "assistant") && !string.IsNullOrWhiteSpace(message.Content))
                messages.Add(new { role = message.Role, content = message.Content });
        }
        messages.Add(new { role = "user", content = userMessage });
        return messages;
    }

    private static Dictionary<string, JsonElement> ParseArguments(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, SerializerOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private const string SystemPrompt = """
        你是 EquityLens，一位專業的投資研究 AI 助手。你作為中樞 Orchestrator，負責理解使用者問題並調度合適的 Agent 執行。

        你可以使用以下工具（由不同的 Agent 提供）：

        重要規則：
        1. 使用繁體中文回答。
        2. 優先使用工具獲取資料，不足時才依據一般知識回答。
        3. 標註來源；不得編造資料。
        4. 根據使用者問題的性質，判斷需要呼叫哪個工具。
        5. 如果問題涉及特定股票的研究分析，使用 research_stock_analysis 工具。
        6. 工具會回傳結構化的研究結果，請將其整理為易讀的回答。
        """;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private sealed record SupervisorLlmResponse(
        IReadOnlyList<SupervisorChoice>? Choices,
        SupervisorUsage? Usage,
        string? Model);

    private sealed record SupervisorChoice(SupervisorMessage? Message);

    private sealed record SupervisorMessage(
        string? Content,
        [property: JsonPropertyName("tool_calls")] IReadOnlyList<SupervisorToolCall>? ToolCalls);

    private sealed record SupervisorToolCall(string Id, string Type, SupervisorFunction Function);

    private sealed record SupervisorFunction(string Name, string Arguments);

    private sealed record SupervisorUsage(
        [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int CompletionTokens);
}
