using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Data;
using EquityLens.Api.Services.Ai.Retrieval;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Services.Agents.Loop;

/// <summary>研究工具只讀取授權來源；每次呼叫有自己的服務 scope。</summary>
public sealed class ResearchLoopTools(IServiceScopeFactory scopes, IOptions<ResearchAgentLoopOptions> options) : IResearchLoopTools
{
    public IReadOnlyList<LoopToolDefinition> Visible(LoopToolScope scope)
    {
        var tools = new List<LoopToolDefinition>();
        if (scope.Request.SourcePolicy != SourcePolicy.WebOnly)
            tools.Add(new("search_local", "搜尋指定公司的本地公開文件，query 必填，可指定 year、topK；documentType 僅接受 AnnualReport 或 EarningsPresentation，不確定時省略。",
                Schema("query", ("query", "string"), ("documentType", "string"), ("year", "integer"), ("topK", "integer")), options.Value.ToolCostReserveUsd));
        if (scope.Request.SourcePolicy != SourcePolicy.LocalOnly &&
            (scope.Request.SourcePolicy != SourcePolicy.LocalThenWeb || scope.LocalSearchCompleted))
            tools.Add(new("search_web", "搜尋公開網頁，query 必填；請含公司與年份。", Schema("query", ("query", "string")), options.Value.ToolCostReserveUsd));
        if (scope.Ledger.Any(x => x.Chunk.SourceType == CitationSourceType.LocalDocument))
            tools.Add(new("get_document_page", "讀取已找到的本地文件頁面，documentId 與 pageNumber 必填。",
                new JsonObject { ["type"] = "object", ["additionalProperties"] = false,
                    ["required"] = new JsonArray("documentId", "pageNumber"),
                    ["properties"] = new JsonObject { ["documentId"] = new JsonObject { ["type"] = "string" }, ["pageNumber"] = new JsonObject { ["type"] = "integer" } } }));
        return tools;
    }

    public string? Validate(LoopToolScope scope, LoopToolCall call)
    {
        if (!Visible(scope).Any(x => x.Name == call.Tool)) return "ToolNotVisible";
        try
        {
            var allowed = call.Tool switch
            {
                "search_local" => new[] { "query", "documentType", "year", "topK" },
                "search_web" => ["query"],
                _ => ["documentId", "pageNumber"]
            };
            if (call.Arguments.Any(x => !allowed.Contains(x.Key))) return "UnknownArgument";
            if (call.Tool.StartsWith("search_", StringComparison.Ordinal))
            {
                var query = call.Arguments["query"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(query) || query.Length > 1000) return "InvalidQuery";
                if (call.Arguments["topK"] is { } topK && (topK.GetValue<int>() < 1 || topK.GetValue<int>() > 10)) return "InvalidTopK";
                if (call.Arguments["year"] is { } year && (year.GetValue<int>() < 1900 || year.GetValue<int>() > 2100)) return "InvalidYear";
                if (call.Arguments["documentType"] is { } type && type.GetValue<string>() is not ("AnnualReport" or "EarningsPresentation")) return "InvalidDocumentType: use AnnualReport or EarningsPresentation, or omit";
            }
            else
            {
                if (!Guid.TryParse(call.Arguments["documentId"]?.GetValue<string>(), out var id)) return "InvalidDocumentId";
                if ((call.Arguments["pageNumber"]?.GetValue<int>() ?? 0) is < 1 or > 10000) return "InvalidPage";
                if (!scope.Ledger.Any(x => x.Chunk.SourceType == CitationSourceType.LocalDocument && x.Chunk.Result.DocumentId == id)) return "DocumentNotInEvidence";
            }
            return null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or System.Text.Json.JsonException)
        { return "InvalidArgumentType"; }
    }

    public async Task<LoopToolResult> InvokeAsync(LoopToolScope scope, LoopToolCall call, CancellationToken ct)
    {
        var denied = Validate(scope, call);
        if (denied is not null) return new([], denied, denied);
        using var serviceScope = scopes.CreateScope();
        var services = serviceScope.ServiceProvider;
        IReadOnlyList<RetrievedDocumentChunk> results;
        if (call.Tool == "search_local")
        {
            var query = call.Arguments["query"]!.GetValue<string>();
            if (call.Arguments["year"] is { } year) query = $"{year.GetValue<int>()} 年 {query}";
            var topK = call.Arguments["topK"]?.GetValue<int>() ?? Math.Clamp(scope.Request.TopK, 1, 10);
            var plan = services.GetRequiredService<IRetrievalPlanner>().BuildPlan(query, scope.Request.RetrievalMode,
                call.Arguments["documentType"]?.GetValue<string>() ?? scope.Request.DocumentType, topK);
            results = await services.GetRequiredService<IDocumentRetriever>().RetrieveAsync(plan, scope.Request.Ticker.Trim().ToUpperInvariant(), ct);
            var intent = services.GetRequiredService<IIntentDetector>().Detect(scope.Request.Question);
            results = (await services.GetRequiredService<IResultReranker>().Rank(results, intent.Selected, topK, ct)).SelectedResults;
        }
        else if (call.Tool == "search_web")
        {
            results = await services.GetRequiredService<IWebRetriever>().RetrieveWebAsync(
                $"{scope.Request.Ticker} {call.Arguments["query"]!.GetValue<string>()}", 6, null, ct);
        }
        else
        {
            var id = Guid.Parse(call.Arguments["documentId"]!.GetValue<string>());
            var page = call.Arguments["pageNumber"]!.GetValue<int>();
            var source = scope.Ledger.First(x => x.Chunk.SourceType == CitationSourceType.LocalDocument && x.Chunk.Result.DocumentId == id).Chunk;
            var db = services.GetRequiredService<EquityLensDbContext>();
            var chunks = await db.DocumentChunks.AsNoTracking().Where(x => x.DocumentId == id && x.PageNumber == page)
                .OrderBy(x => x.ChunkIndex).Take(10).ToListAsync(ct);
            results = chunks.Select(x => source with { Result = source.Result with { DocumentChunkId = x.Id, ChunkIndex = x.ChunkIndex,
                Content = x.Content, PageNumber = x.PageNumber, SectionTitle = x.SectionTitle } }).ToList();
        }
        return new(results, $"Found {results.Count} evidence items.");
    }

    private static JsonObject Schema(string required, params (string Name, string Type)[] properties) => new()
    {
        ["type"] = "object", ["additionalProperties"] = false, ["required"] = new JsonArray(required),
        ["properties"] = new JsonObject(properties.Select(x => new KeyValuePair<string, JsonNode?>(x.Name, x.Name == "documentType" ? new JsonObject { ["type"] = x.Type, ["enum"] = new JsonArray("AnnualReport", "EarningsPresentation") } : new JsonObject { ["type"] = x.Type })))
    };
}
