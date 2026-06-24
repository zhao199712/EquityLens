using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Observability;
using EquityLens.Api.Services.Ai;
using EquityLens.Api.Services.Documents;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Services.Chat;

public sealed class DeepSeekChatAgentService : IChatAgentService
{
    private const int MaxIterations = 10;
    private readonly HttpClient _httpClient;
    private readonly DeepSeekOptions _options;
    private readonly IDocumentSearchService _documentSearchService;
    private readonly IFinancialDataService _financialDataService;
    private readonly IJinaSearchService _jinaSearchService;
    private readonly ILogger<DeepSeekChatAgentService> _logger;

    public DeepSeekChatAgentService(
        HttpClient httpClient,
        IOptions<DeepSeekOptions> options,
        IDocumentSearchService documentSearchService,
        IFinancialDataService financialDataService,
        IJinaSearchService jinaSearchService,
        ILogger<DeepSeekChatAgentService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _documentSearchService = documentSearchService;
        _financialDataService = financialDataService;
        _jinaSearchService = jinaSearchService;
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

        var model = string.IsNullOrWhiteSpace(_options.Model) ? "deepseek-v4-flash" : _options.Model;
        var messages = BuildMessages(request.History, request.UserMessage);
        var promptTokens = 0;
        var completionTokens = 0;

        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            using var activity = EquityLensTelemetry.ActivitySource.StartActivity("chat.llm.request");
            activity?.SetTag("chat.provider", "deepseek");
            activity?.SetTag("chat.model", model);
            activity?.SetTag("chat.iteration", iteration);
            activity?.SetTag("chat.session_id", request.SessionId);

            var stopwatch = Stopwatch.StartNew();
            DeepSeekAgentResponse? result = null;
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
                        tools = BuildTools(),
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
                    activity?.SetTag("chat.http_status", (int)response.StatusCode);
                    activity?.SetStatus(ActivityStatusCode.Error, response.StatusCode.ToString());
                    _logger.LogError("DeepSeek API error: {StatusCode}", response.StatusCode);
                    apiError = $"DeepSeek API error: {response.StatusCode}";
                }
                else
                {
                    result = JsonSerializer.Deserialize<DeepSeekAgentResponse>(responseBody, SerializerOptions);
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
                yield return new ChatAgentStreamEvent.Error("DeepSeek returned no choices.");
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

                using var toolActivity = EquityLensTelemetry.ActivitySource.StartActivity("chat.tool.execute");
                toolActivity?.SetTag("chat.tool.name", toolCall.Function.Name);
                toolActivity?.SetTag("chat.session_id", request.SessionId);
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

        yield return new ChatAgentStreamEvent.Error("Max tool-calling iterations reached.");
    }

    private async Task<string> ExecuteToolAsync(
        string toolName,
        Dictionary<string, JsonElement> arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            return toolName switch
            {
                "searchDocuments" => await SearchDocumentsAsync(arguments, cancellationToken),
                "queryFinancialData" => await QueryFinancialDataAsync(arguments, cancellationToken),
                "webSearch" => await WebSearchAsync(arguments, cancellationToken),
                _ => $"Unknown tool: {toolName}"
            };
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Tool execution failed: {ToolName}", toolName);
            return $"Tool execution error: {exception.Message}";
        }
    }

    private async Task<string> SearchDocumentsAsync(
        Dictionary<string, JsonElement> arguments,
        CancellationToken cancellationToken)
    {
        var response = await _documentSearchService.SearchAsync(
            new Contracts.Research.DocumentSearchRequest(
                Query: arguments.GetStringOrDefault("query", ""),
                Ticker: arguments.GetStringOrDefault("ticker", null),
                DocumentType: arguments.GetStringOrDefault("documentType", null),
                TopK: 5),
            cancellationToken);
        if (response.Results.Count == 0) return "No relevant documents found.";

        var output = new StringBuilder();
        foreach (var result in response.Results)
        {
            output.AppendLine($"[{result.DocumentTitle}] (Type: {result.DocumentType}, Score: {result.RelevanceScore:F2})");
            output.AppendLine(result.Content);
            output.AppendLine("---");
        }
        return output.ToString();
    }

    private async Task<string> QueryFinancialDataAsync(
        Dictionary<string, JsonElement> arguments,
        CancellationToken cancellationToken)
    {
        var ticker = arguments.GetStringOrDefault("ticker", "");
        var statementType = arguments.GetStringOrDefault("statementType", "IncomeStatement");
        var fiscalYear = arguments.GetIntOrDefault("fiscalYear", DateTime.Now.Year - 1);
        var fiscalQuarter = arguments.GetNullableIntOrDefault("fiscalQuarter", null);
        var results = await _financialDataService.QueryAsync(
            new FinancialDataQuery(ticker, statementType, fiscalYear, fiscalQuarter), cancellationToken);
        if (results.Count == 0) return $"No financial data found for {ticker} ({statementType} FY{fiscalYear}).";

        var output = new StringBuilder();
        foreach (var result in results)
        {
            output.AppendLine($"{result.Ticker} - {result.StatementType} ({result.PeriodType} FY{result.FiscalYear}) - {result.Currency}");
            foreach (var item in result.LineItems)
                output.AppendLine($"  {item.Code} ({item.Name}): {item.Amount:N2} {item.Unit}");
        }
        return output.ToString();
    }

    private async Task<string> WebSearchAsync(
        Dictionary<string, JsonElement> arguments,
        CancellationToken cancellationToken)
    {
        var response = await _jinaSearchService.SearchAsync(
            arguments.GetStringOrDefault("query", ""), 5, cancellationToken);
        if (response.Results.Count == 0) return "No web search results found.";

        var output = new StringBuilder();
        foreach (var result in response.Results)
        {
            output.AppendLine($"[{result.Title}]({result.Url})");
            output.AppendLine(result.Content.Length > 2000 ? result.Content[..2000] + "..." : result.Content);
            output.AppendLine("---");
        }
        return output.ToString();
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

    private static object[] BuildTools() =>
    [
        Tool("searchDocuments", "Search financial reports and earnings presentations.", new
        {
            type = "object",
            properties = new
            {
                query = new { type = "string", description = "Natural-language search query" },
                ticker = new { type = "string", description = "Stock ticker, for example 2330" },
                documentType = new { type = "string", description = "AnnualReport or EarningsPresentation" }
            },
            required = new[] { "query" }
        }),
        Tool("queryFinancialData", "Query structured financial statement data.", new
        {
            type = "object",
            properties = new
            {
                ticker = new { type = "string" },
                statementType = new { type = "string", description = "IncomeStatement, BalanceSheet, or CashFlow" },
                fiscalYear = new { type = "integer" },
                fiscalQuarter = new { type = "integer" }
            },
            required = new[] { "ticker", "statementType", "fiscalYear" }
        }),
        Tool("webSearch", "Search the web when internal data is insufficient.", new
        {
            type = "object",
            properties = new { query = new { type = "string" } },
            required = new[] { "query" }
        })
    ];

    private static object Tool(string name, string description, object parameters) => new
    {
        type = "function",
        function = new { name, description, parameters }
    };

    private const string SystemPrompt = """
        你是 EquityLens，一位專業的投資研究 AI 助手。你可以使用 searchDocuments、queryFinancialData、webSearch 工具。
        使用繁體中文回答；優先使用內部財報與法說會資料，不足時才搜尋網路；標註來源；不得編造資料。
        """;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private sealed record DeepSeekAgentResponse(
        IReadOnlyList<DeepSeekChoice>? Choices,
        DeepSeekUsage? Usage,
        string? Model);
    private sealed record DeepSeekChoice(DeepSeekMessage? Message);
    private sealed record DeepSeekMessage(
        string? Content,
        [property: JsonPropertyName("tool_calls")] IReadOnlyList<DeepSeekToolCall>? ToolCalls);
    private sealed record DeepSeekToolCall(string Id, string Type, DeepSeekFunction Function);
    private sealed record DeepSeekFunction(string Name, string Arguments);
    private sealed record DeepSeekUsage(
        [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int CompletionTokens);
}
