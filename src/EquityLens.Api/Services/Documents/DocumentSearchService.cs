using System.Data;
using System.Globalization;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Data;
using EquityLens.Api.Observability;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.Documents;

public sealed class DocumentSearchService : IDocumentSearchService
{
    private const int DefaultTopK = 8;
    private const int MaxTopK = 20;
    private readonly EquityLensDbContext _dbContext;
    private readonly IEmbeddingService _embeddingService;

    public DocumentSearchService(EquityLensDbContext dbContext, IEmbeddingService embeddingService)
    {
        _dbContext = dbContext;
        _embeddingService = embeddingService;
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
        var embeddings = await _embeddingService.CreateEmbeddingsAsync([query], cancellationToken);
        var queryEmbedding = ToPgVectorLiteral(embeddings[0]);

        using var activity = EquityLensTelemetry.ActivitySource.StartActivity("vector_search");
        activity?.SetTag("embedding.model", _embeddingService.Model);
        activity?.SetTag("retrieval.top_k", topK);
        activity?.SetTag("document.type", documentType);

        try
        {
            var results = new List<DocumentSearchResult>();
            var connection = _dbContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
            }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            select
                dc.id as document_chunk_id,
                d.id as document_id,
                d.title as document_title,
                d.document_type,
                coalesce(ic.source_url, ff.source_url, d.source_url) as source_url,
                dc.chunk_index,
                dc.page_number,
                dc.section_title,
                dc.content,
                (de.embedding <=> @query_embedding::vector) as distance,
                s.id as security_id,
                s.ticker,
                s.exchange,
                s.name as security_name
            from document_embedding de
            inner join document_chunk dc on dc.id = de.document_chunk_id
            inner join document d on d.id = dc.document_id
            left join investor_conference ic on ic.document_id = d.id
            left join financial_filing ff on ff.document_id = d.id
            left join security s on s.id = coalesce(ic.security_id, ff.security_id)
            where (@ticker::text is null or upper(s.ticker) = upper(@ticker::text))
              and (@document_type::text is null or d.document_type = @document_type::text)
            order by de.embedding <=> @query_embedding::vector
            limit @top_k;
            """;

        var embeddingParameter = command.CreateParameter();
        embeddingParameter.ParameterName = "query_embedding";
        embeddingParameter.Value = queryEmbedding;
        command.Parameters.Add(embeddingParameter);

        var tickerParameter = command.CreateParameter();
        tickerParameter.ParameterName = "ticker";
        tickerParameter.Value = ticker is null ? DBNull.Value : ticker;
        command.Parameters.Add(tickerParameter);

        var documentTypeParameter = command.CreateParameter();
        documentTypeParameter.ParameterName = "document_type";
        documentTypeParameter.Value = documentType is null ? DBNull.Value : documentType;
        command.Parameters.Add(documentTypeParameter);

        var topKParameter = command.CreateParameter();
        topKParameter.ParameterName = "top_k";
        topKParameter.Value = topK;
        command.Parameters.Add(topKParameter);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var distance = reader.GetDouble(reader.GetOrdinal("distance"));
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
                distance,
                1 - distance,
                GetNullableGuid(reader, "security_id"),
                GetNullableString(reader, "ticker"),
                GetNullableString(reader, "exchange"),
                GetNullableString(reader, "security_name")));
        }

            activity?.SetTag("result.count", results.Count);
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
