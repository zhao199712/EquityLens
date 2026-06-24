using System.Data;
using System.Globalization;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Data;
using EquityLens.Api.Observability;
using EquityLens.Api.Services.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Services.Documents;

public sealed class DocumentSearchService : IDocumentSearchService
{
    private const int DefaultTopK = 8;
    private const int MaxTopK = 20;
    private readonly EquityLensDbContext _dbContext;
    private readonly IEmbeddingService _embeddingService;
    private readonly RetrievalOptions _options;

    public DocumentSearchService(EquityLensDbContext dbContext, IEmbeddingService embeddingService, IOptions<RetrievalOptions> options)
    {
        _dbContext = dbContext;
        _embeddingService = embeddingService;
        _options = options.Value;
    }

    public async Task<DocumentSearchResponse> SearchAsync(DocumentSearchRequest request, CancellationToken cancellationToken = default)
    {
        var query = request.Query.Trim();
        if (query.Length == 0)
        {
            throw new ArgumentException("查詢內容不可為空。", nameof(request));
        }

        var topK = Math.Clamp(request.TopK ?? DefaultTopK, 1, MaxTopK);
        var ticker = string.IsNullOrWhiteSpace(request.Ticker) ? null : request.Ticker.Trim();
        var documentType = string.IsNullOrWhiteSpace(request.DocumentType)
            || string.Equals(request.DocumentType.Trim(), "auto", StringComparison.OrdinalIgnoreCase)
            ? null
            : request.DocumentType.Trim();
        var mode = _options.RetrievalMode switch
        {
            "VectorOnly" => "vector",
            "Bm25Only" => "bm25",
            "Hybrid" => "hybrid",
            _ => "vector"
        };

        var embeddings = mode != "bm25"
            ? await _embeddingService.CreateEmbeddingsAsync([query], cancellationToken)
            : [];
        var queryEmbedding = embeddings.Count > 0 ? ToPgVectorLiteral(embeddings[0]) : "";

        using var activity = EquityLensTelemetry.ActivitySource.StartActivity("document_search");
        activity?.SetTag("embedding.model", _embeddingService.Model);
        activity?.SetTag("retrieval.top_k", topK);
        activity?.SetTag("document.type", documentType);
        activity?.SetTag("retrieval.mode", mode);

        try
        {
            var results = new List<DocumentSearchResult>();
            var connection = _dbContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM search_document_chunks(" +
                "query_text := @query_text, " +
                "query_embedding := @query_embedding::vector, " +
                "ticker_filter := @ticker, " +
                "document_type_filter := @document_type, " +
                "top_k := @top_k, " +
                "mode := @mode)";

            var queryTextParam = command.CreateParameter();
            queryTextParam.ParameterName = "query_text";
            queryTextParam.Value = mode == "vector" ? DBNull.Value : query;
            command.Parameters.Add(queryTextParam);

            var embeddingParam = command.CreateParameter();
            embeddingParam.ParameterName = "query_embedding";
            embeddingParam.Value = mode == "bm25" ? DBNull.Value : queryEmbedding;
            command.Parameters.Add(embeddingParam);

            var tickerParam = command.CreateParameter();
            tickerParam.ParameterName = "ticker";
            tickerParam.Value = ticker is null ? DBNull.Value : ticker;
            command.Parameters.Add(tickerParam);

            var documentTypeParam = command.CreateParameter();
            documentTypeParam.ParameterName = "document_type";
            documentTypeParam.Value = documentType is null ? DBNull.Value : documentType;
            command.Parameters.Add(documentTypeParam);

            var topKParam = command.CreateParameter();
            topKParam.ParameterName = "top_k";
            topKParam.Value = topK;
            command.Parameters.Add(topKParam);

            var modeParam = command.CreateParameter();
            modeParam.ParameterName = "mode";
            modeParam.Value = mode;
            command.Parameters.Add(modeParam);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var score = reader.GetDouble(reader.GetOrdinal("score"));
                results.Add(new DocumentSearchResult(
                    reader.GetGuid(reader.GetOrdinal("document_chunk_id")),
                    reader.GetGuid(reader.GetOrdinal("document_id")),
                    reader.GetString(reader.GetOrdinal("document_title")),
                    GetNullableString(reader, "document_type"),
                    GetNullableString(reader, "source_url"),
                    reader.GetInt32(reader.GetOrdinal("chunk_index")),
                    GetNullableInt32(reader, "page_number"),
                    GetNullableString(reader, "section_title"),
                    reader.GetString(reader.GetOrdinal("content")),
                    score,
                    score,
                    GetNullableGuid(reader, "security_id"),
                    GetNullableString(reader, "ticker"),
                    GetNullableString(reader, "exchange"),
                    GetNullableString(reader, "security_name")));
            }

            activity?.SetTag("result.count", results.Count);
            activity?.SetTag("retrieval.mode_used", mode);
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Ok);
            return new DocumentSearchResponse(query, _embeddingService.Model, results);
        }
        catch (Exception exception)
        {
            EquityLensTelemetry.MarkError(activity, exception);
            throw;
        }
    }

    private static string ToPgVectorLiteral(IReadOnlyList<float> values)
    {
        return "[" + string.Join(',', values.Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + "]";
    }

    private static string? GetNullableString(IDataRecord reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static Guid? GetNullableGuid(IDataRecord reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    private static int? GetNullableInt32(IDataRecord reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }
}
