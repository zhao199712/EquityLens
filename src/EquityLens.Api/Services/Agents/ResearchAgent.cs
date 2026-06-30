using System.Diagnostics;
using System.Text.Json;
using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Observability;
using EquityLens.Api.Services.Ai;
using EquityLens.Api.Services.Research;

namespace EquityLens.Api.Services.Agents;

/// <summary>
/// 封裝 EquityLens Ask Pipeline 為單一 Agent。
/// 內部同步執行 7 階段 pipeline（Intent → Retrieval → Vector → Merge/Rerank → Selection/Dedup → Generation），
/// 回傳結構化的 AgentResponse（含 Citations, Strategy, Trace）。
/// </summary>
public sealed class ResearchAgent : IAgent
{
    private const string ToolName = "research_stock_analysis";

    private readonly IResearchPreflightService _preflightService;
    private readonly IResearchAnswerService _answerService;
    private readonly ILogger<ResearchAgent> _logger;

    public string Name => "ResearchAgent";
    public string Description => "分析指定股票的研究問題，基於年報與法說會文件提供深度分析。";

    public ResearchAgent(
        IResearchPreflightService preflightService,
        IResearchAnswerService answerService,
        ILogger<ResearchAgent> logger)
    {
        _preflightService = preflightService;
        _answerService = answerService;
        _logger = logger;
    }

    public IReadOnlyList<AgentToolDefinition> GetTools()
    {
        return
        [
            new AgentToolDefinition(
                ToolName,
                "分析指定股票的研究問題。基於年報與法說會文件進行深度分析，回傳含引用來源的研究報告。需要股票代號（ticker）和問題（question）。",
                new
                {
                    type = "object",
                    properties = new
                    {
                        ticker = new { type = "string", description = "股票代號，例如 2330" },
                        question = new { type = "string", description = "研究問題，例如「分析台積電的風險因素」" },
                        retrievalMode = new
                        {
                            type = "string",
                            @enum = new[] { "Auto", "ConferenceOnly", "AnnualReportOnly", "AllDocuments" },
                            description = "檢索模式：Auto（自動混合）、ConferenceOnly（僅法說會）、AnnualReportOnly（僅年報）、AllDocuments（全部）"
                        },
                        documentType = new
                        {
                            type = "string",
                            @enum = new[] { "AnnualReport", "EarningsPresentation" },
                            description = "文件類型篩選"
                        },
                        sourcePolicy = new
                        {
                            type = "string",
                            @enum = new[] { "LocalOnly", "WebOnly", "LocalAndWeb", "LocalThenWeb" },
                            description = "資料來源策略：LocalOnly（僅本地）、WebOnly（僅網路）、LocalAndWeb（本地+網路）、LocalThenWeb（優先本地，不足時網路）"
                        },
                        topK = new { type = "integer", description = "返回結果數量（1-20），預設 8" },
                        temperature = new { type = "number", description = "生成溫度（0.0-2.0），預設 0.2" },
                        debug = new { type = "boolean", description = "是否回傳 debug trace 資訊" }
                    },
                    required = new[] { "ticker", "question" }
                })
        ];
    }

    public async Task<AgentResponse> ExecuteToolAsync(
        string toolName,
        Dictionary<string, JsonElement> args,
        CancellationToken cancellationToken = default)
    {
        if (toolName != ToolName)
        {
            return new AgentResponse(
                Content: $"Unknown tool: {toolName}",
                Citations: [],
                Strategy: null,
                Trace: null,
                Status: "Error",
                ToolCalls: [],
                Model: string.Empty,
                PromptTokens: 0,
                CompletionTokens: 0);
        }

        var ticker = args.GetStringOrDefault("ticker", string.Empty);
        var question = args.GetStringOrDefault("question", string.Empty);
        var retrievalModeStr = args.GetStringOrDefault("retrievalMode", null);
        var documentType = args.GetStringOrDefault("documentType", null);
        var sourcePolicyStr = args.GetStringOrDefault("sourcePolicy", "LocalOnly");
        var topK = args.GetIntOrDefault("topK", 8);
        var temperature = args.GetDoubleOrDefault("temperature", 0.2);
        var debug = args.GetBoolOrDefault("debug", false);

        using var activity = EquityLensTelemetry.ActivitySource.StartActivity("agent.research.execute");
        activity?.SetTag("agent.name", Name);
        activity?.SetTag("research.ticker", ticker);
        activity?.SetTag("research.question", question);

        var request = new ResearchAskRequest(
            Ticker: ticker,
            Question: question,
            RetrievalMode: Enum.TryParse<RetrievalMode>(retrievalModeStr, true, out var mode) ? mode : null,
            DocumentType: documentType,
            SourcePolicy: Enum.TryParse<SourcePolicy>(sourcePolicyStr, true, out var policy) ? policy : SourcePolicy.LocalOnly,
            TopK: topK,
            Temperature: temperature,
            Debug: debug);

        var preflightResult = await _preflightService.ValidateAskAsync(request, cancellationToken);
        if (!preflightResult.IsSuccess)
        {
            _logger.LogWarning("Preflight validation failed for {Ticker}: {Error}", ticker, preflightResult.ErrorMessage);
            activity?.SetTag("research.preflight_error", preflightResult.ErrorCode);
            activity?.SetStatus(ActivityStatusCode.Ok);

            return new AgentResponse(
                Content: preflightResult.ErrorMessage ?? "Preflight validation failed.",
                Citations: [],
                Strategy: null,
                Trace: null,
                Status: "PreflightFailed",
                ToolCalls: [],
                Model: string.Empty,
                PromptTokens: 0,
                CompletionTokens: 0);
        }

        var askResponse = await _answerService.AskAsync(request, cancellationToken);

        activity?.SetTag("research.status", askResponse.Status);
        activity?.SetTag("research.citation_count", askResponse.Citations.Count);
        activity?.SetStatus(ActivityStatusCode.Ok);

        _logger.LogInformation(
            "ResearchAgent completed for {Ticker}: {Status}, {CitationCount} citations",
            ticker,
            askResponse.Status,
            askResponse.Citations.Count);

        return new AgentResponse(
            Content: askResponse.Answer,
            Citations: askResponse.Citations,
            Strategy: askResponse.RetrievalStrategy,
            Trace: askResponse.Trace,
            Status: askResponse.Status,
            ToolCalls:
            [
                new AgentToolCall(ToolName, JsonSerializer.Serialize(args), $"Answered with {askResponse.Citations.Count} citations")
            ],
            Model: askResponse.Model,
            PromptTokens: askResponse.Trace?.TokenUsage.PromptTokens ?? 0,
            CompletionTokens: askResponse.Trace?.TokenUsage.CompletionTokens ?? 0);
    }
}
