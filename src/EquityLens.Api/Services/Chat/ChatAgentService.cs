using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Observability;
using EquityLens.Api.Services.Ai;
using EquityLens.Api.Services.Documents;

namespace EquityLens.Api.Services.Chat;

public sealed class ChatAgentService : IChatAgentService
{
    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _options;
    private readonly IDocumentSearchService _documentSearchService;
    private readonly IFinancialDataService _financialDataService;
    private readonly IJinaSearchService _jinaSearchService;
    private readonly ILogger<ChatAgentService> _logger;

    private const int MaxIterations = 10;

    public ChatAgentService(
        HttpClient httpClient,
        IOptions<GeminiOptions> options,
        IDocumentSearchService documentSearchService,
        IFinancialDataService financialDataService,
        IJinaSearchService jinaSearchService,
        ILogger<ChatAgentService> logger)
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
            ? Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            : _options.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Gemini API key is not configured.");

        var model = string.IsNullOrWhiteSpace(_options.Model) ? "gemini-2.0-flash" : _options.Model;
        var contents = BuildContents(request.History, request.UserMessage);
        var tools = BuildTools();
        var systemInstruction = BuildSystemPrompt();

        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            using var llmActivity = EquityLensTelemetry.ActivitySource.StartActivity("chat.llm.request");
            llmActivity?.SetTag("chat.model", model);
            llmActivity?.SetTag("chat.iteration", iteration);
            llmActivity?.SetTag("chat.session_id", request.SessionId);

            var llmStopwatch = System.Diagnostics.Stopwatch.StartNew();

            var requestBody = JsonSerializer.Serialize(new
            {
                systemInstruction = new { parts = new[] { new { text = systemInstruction } } },
                contents,
                tools,
                generationConfig = new { temperature = 0.3, maxOutputTokens = 4096 }
            }, SerializerOptions);

            var endpoint = $"models/{Uri.EscapeDataString(model)}:generateContent";
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
            httpRequest.Headers.Add("x-goog-api-key", apiKey);
            httpRequest.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            llmStopwatch.Stop();
            EquityLensTelemetry.ChatLlmDuration.Record(llmStopwatch.Elapsed.TotalMilliseconds,
                new TagList { { "model", model }, { "status", response.IsSuccessStatusCode ? "success" : "error" } });

            if (!response.IsSuccessStatusCode)
            {
                llmActivity?.SetTag("chat.http_status", (int)response.StatusCode);
                _logger.LogError("Gemini API error: {StatusCode} {Body}", response.StatusCode, responseBody);
                var errorDetail = responseBody.Length > 300 ? responseBody[..300] + "..." : responseBody;
                yield return new ChatAgentStreamEvent.Error($"Gemini API error: {response.StatusCode} - {errorDetail}");
                yield break;
            }

            var geminiResponse = JsonSerializer.Deserialize<GeminiResponse>(responseBody, SerializerOptions);
            if (geminiResponse?.Candidates == null || geminiResponse.Candidates.Count == 0)
            {
                yield return new ChatAgentStreamEvent.Error("Gemini returned no candidates.");
                yield break;
            }

            var candidate = geminiResponse.Candidates[0];
            if (candidate.Content?.Parts == null)
            {
                yield return new ChatAgentStreamEvent.Error("Gemini returned empty content.");
                yield break;
            }

            var functionCalls = candidate.Content.Parts
                .Where(p => p.FunctionCall != null)
                .ToList();

            var textParts = candidate.Content.Parts
                .Where(p => !string.IsNullOrEmpty(p.Text))
                .ToList();

            if (functionCalls.Count == 0)
            {
                foreach (var part in textParts)
                    yield return new ChatAgentStreamEvent.TextDelta(part.Text!);

                yield return new ChatAgentStreamEvent.Done(
                    model,
                    geminiResponse.UsageMetadata?.PromptTokenCount ?? 0,
                    geminiResponse.UsageMetadata?.CandidatesTokenCount ?? 0);
                yield break;
            }

            // Preserve original model parts (with thoughtSignature) for the model turn
            var modelParts = candidate.Content.Parts
                .Where(p => p.FunctionCall != null)
                .Select(p => (object)new
                {
                    functionCall = new { name = p.FunctionCall!.Name, args = p.FunctionCall.Args },
                    thoughtSignature = p.ThoughtSignature
                })
                .ToArray();
            contents.Add(new { role = "model", parts = modelParts });

            // Execute tools and collect function responses
            var functionResponseParts = new List<object>();
            foreach (var fc in functionCalls)
            {
                var toolName = fc.FunctionCall!.Name;
                var argsJson = JsonSerializer.Serialize(fc.FunctionCall.Args, SerializerOptions);

                using var toolActivity = EquityLensTelemetry.ActivitySource.StartActivity("chat.tool.execute");
                toolActivity?.SetTag("chat.tool.name", toolName);
                toolActivity?.SetTag("chat.session_id", request.SessionId);

                var toolStopwatch = System.Diagnostics.Stopwatch.StartNew();

                yield return new ChatAgentStreamEvent.ToolCallStart(toolName, argsJson);
                var toolResult = await ExecuteToolAsync(toolName, fc.FunctionCall.Args, cancellationToken);
                var preview = toolResult.Length > 500 ? toolResult[..500] + "..." : toolResult;

                toolStopwatch.Stop();
                EquityLensTelemetry.ChatToolCalls.Add(1, new TagList { { "tool", toolName } });
                EquityLensTelemetry.ChatToolDuration.Record(toolStopwatch.Elapsed.TotalMilliseconds,
                    new TagList { { "tool", toolName } });

                yield return new ChatAgentStreamEvent.ToolCallEnd(toolName, preview);

                functionResponseParts.Add(new
                {
                    functionResponse = new { name = toolName, response = new { result = toolResult } }
                });
            }
            contents.Add(new { role = "user", parts = functionResponseParts.ToArray() });
        }

        yield return new ChatAgentStreamEvent.Error("Max tool-calling iterations reached.");
    }

    private async Task<string> ExecuteToolAsync(
        string toolName, Dictionary<string, JsonElement> args, CancellationToken ct)
    {
        try
        {
            return toolName switch
            {
                "searchDocuments" => await SearchDocumentsAsync(args, ct),
                "queryFinancialData" => await QueryFinancialDataAsync(args, ct),
                "webSearch" => await WebSearchAsync(args, ct),
                _ => $"Unknown tool: {toolName}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool execution failed: {ToolName}", toolName);
            return $"Tool execution error: {ex.Message}";
        }
    }

    private async Task<string> SearchDocumentsAsync(Dictionary<string, JsonElement> args, CancellationToken ct)
    {
        var query = args.GetStringOrDefault("query", "");
        var ticker = args.GetStringOrDefault("ticker", null);
        var docType = args.GetStringOrDefault("documentType", null);

        var response = await _documentSearchService.SearchAsync(
            new Contracts.Research.DocumentSearchRequest(Query: query, Ticker: ticker, DocumentType: docType, TopK: 5), ct);

        if (response.Results.Count == 0) return "No relevant documents found.";

        var sb = new StringBuilder();
        foreach (var r in response.Results)
        {
            sb.AppendLine($"[{r.DocumentTitle}] (Type: {r.DocumentType}, Score: {r.RelevanceScore:F2})");
            sb.AppendLine(r.Content);
            sb.AppendLine("---");
        }
        return sb.ToString();
    }

    private async Task<string> QueryFinancialDataAsync(Dictionary<string, JsonElement> args, CancellationToken ct)
    {
        var ticker = args.GetStringOrDefault("ticker", "");
        var statementType = args.GetStringOrDefault("statementType", "IncomeStatement");
        var fiscalYear = args.GetIntOrDefault("fiscalYear", DateTime.Now.Year - 1);
        var fiscalQuarter = args.GetNullableIntOrDefault("fiscalQuarter", null);

        var results = await _financialDataService.QueryAsync(
            new FinancialDataQuery(ticker, statementType, fiscalYear, fiscalQuarter), ct);

        if (results.Count == 0) return $"No financial data found for {ticker} ({statementType} FY{fiscalYear}).";

        var sb = new StringBuilder();
        foreach (var r in results)
        {
            sb.AppendLine($"{r.Ticker} - {r.StatementType} ({r.PeriodType} FY{r.FiscalYear}{(r.FiscalQuarter.HasValue ? $" Q{r.FiscalQuarter}" : "")}) - {r.Currency}");
            foreach (var li in r.LineItems)
                sb.AppendLine($"  {li.Code} ({li.Name}): {li.Amount:N2} {li.Unit}");
        }
        return sb.ToString();
    }

    private async Task<string> WebSearchAsync(Dictionary<string, JsonElement> args, CancellationToken ct)
    {
        var query = args.GetStringOrDefault("query", "");
        var response = await _jinaSearchService.SearchAsync(query, 5, ct);
        if (response.Results.Count == 0) return "No web search results found.";

        var sb = new StringBuilder();
        foreach (var r in response.Results)
        {
            sb.AppendLine($"[{r.Title}]({r.Url})");
            sb.AppendLine(r.Content.Length > 2000 ? r.Content[..2000] + "..." : r.Content);
            sb.AppendLine("---");
        }
        return sb.ToString();
    }

    private static string BuildSystemPrompt() => """
        你是 EquityLens，一位專業的投資研究 AI 助手。你可以使用以下工具來回答使用者的問題：

        ## 工具說明

        1. **searchDocuments**: 在財報 PDF 和法說會文件中搜尋相關內容。
           - 適用於：公司營運、風險、展望、管理層討論等定性資訊
           - 參數：query (必填), ticker (選填), documentType (選填: AnnualReport, EarningsPresentation)

        2. **queryFinancialData**: 查詢結構化財務報表數據（損益表、資產負債表、現金流量表）。
           - 適用於：營收、毛利、淨利、資產、負債等具體數字
           - 參數：ticker (必填), statementType (必填: IncomeStatement/BalanceSheet/CashFlow), fiscalYear (必填), fiscalQuarter (選填)

        3. **webSearch**: 搜尋網路取得最新資訊。
           - 適用於：資料庫中沒有的最新消息、市場動態、產業趨勢
           - 參數：query (必填)

        ## 回答規則

        1. 使用繁體中文回答。
        2. 優先使用資料庫中的財報和法說會資料。
        3. 如果資料庫資料不足，再使用 webSearch 查詢最新資訊。
        4. 回答時標註資料來源 [1], [2] 等。
        5. 不要編造不存在的數據或資訊。
        6. 保持客觀、專業的分析語調。
        """;

    private static List<object> BuildContents(IReadOnlyList<ChatMessage> history, string userMessage)
    {
        var contents = new List<object>();
        var recentHistory = history.Skip(Math.Max(0, history.Count - 20)).ToList();

        foreach (var msg in recentHistory)
        {
            if (msg.Role == "user")
                contents.Add(new { role = "user", parts = new[] { new { text = msg.Content } } });
            else if (msg.Role == "assistant" && !string.IsNullOrEmpty(msg.Content))
                contents.Add(new { role = "model", parts = new[] { new { text = msg.Content } } });
        }

        contents.Add(new { role = "user", parts = new[] { new { text = userMessage } } });
        return contents;
    }

    private static object[] BuildTools() => new object[]
    {
        new
        {
            functionDeclarations = new object[]
            {
                new {
                    name = "searchDocuments",
                    description = "Search financial reports (annual reports) and investor conference (earnings call) documents for relevant content.",
                    parameters = new {
                        type = "OBJECT",
                        properties = new {
                            query = new { type = "STRING", description = "Search query in natural language" },
                            ticker = new { type = "STRING", description = "Stock ticker symbol, e.g. 2330, AAPL" },
                            documentType = new { type = "STRING", description = "Document type filter: AnnualReport or EarningsPresentation" }
                        },
                        required = new[] { "query" }
                    }
                },
                new {
                    name = "queryFinancialData",
                    description = "Query structured financial statement data (income statement, balance sheet, cash flow) for a specific company and period.",
                    parameters = new {
                        type = "OBJECT",
                        properties = new {
                            ticker = new { type = "STRING", description = "Stock ticker symbol, e.g. 2330, AAPL" },
                            statementType = new { type = "STRING", description = "Statement type: IncomeStatement, BalanceSheet, or CashFlow" },
                            fiscalYear = new { type = "INTEGER", description = "Fiscal year, e.g. 2024" },
                            fiscalQuarter = new { type = "INTEGER", description = "Fiscal quarter (1-4), null for annual" }
                        },
                        required = new[] { "ticker", "statementType", "fiscalYear" }
                    }
                },
                new {
                    name = "webSearch",
                    description = "Search the web for the latest information when database content is insufficient.",
                    parameters = new {
                        type = "OBJECT",
                        properties = new {
                            query = new { type = "STRING", description = "Search query" }
                        },
                        required = new[] { "query" }
                    }
                }
            }
        }
    };

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
}
