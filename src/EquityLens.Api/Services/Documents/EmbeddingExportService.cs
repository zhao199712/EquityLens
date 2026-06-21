using System.Text.Json;
using EquityLens.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.Documents;

public sealed class EmbeddingExportService : IEmbeddingExportService
{
    private readonly EquityLensDbContext _dbContext;

    public EmbeddingExportService(EquityLensDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<EmbeddingExportResult> ExportAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var rows = await _dbContext.DocumentEmbeddings
            .Include(e => e.DocumentChunk)
                .ThenInclude(c => c.Document)
                    .ThenInclude(d => d.UploadedFile)
            .OrderBy(e => e.DocumentChunk.DocumentId)
            .ThenBy(e => e.DocumentChunk.ChunkIndex)
            .ToListAsync(cancellationToken);

        var documentIds = rows.Select(e => e.DocumentChunk.DocumentId).Distinct().ToList();
        var conferences = await _dbContext.InvestorConferences
            .Include(c => c.Security)
            .Where(c => c.DocumentId != null && documentIds.Contains(c.DocumentId.Value))
            .ToDictionaryAsync(c => c.DocumentId!.Value, cancellationToken);

        await using var file = File.CreateText(fullPath);
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunk = row.DocumentChunk;
            var document = chunk.Document;
            conferences.TryGetValue(document.Id, out var conference);

            var exportRow = new
            {
                documentId = document.Id,
                chunkId = chunk.Id,
                ticker = conference?.Security.Ticker,
                companyName = conference?.Security.Name,
                exchange = conference?.Security.Exchange,
                documentType = document.DocumentType,
                conferenceDate = conference?.EventDate?.ToString("yyyy-MM-dd"),
                language = document.Language,
                pageNumber = chunk.PageNumber,
                chunkIndex = chunk.ChunkIndex,
                content = chunk.Content,
                contentHash = chunk.ContentHash,
                embeddingModel = row.EmbeddingModel,
                embeddingDimensions = row.Dimensions,
                sourceObjectKey = document.UploadedFile.ObjectKey,
                originalFileName = document.UploadedFile.OriginalFileName,
                embedding = row.Embedding.ToArray()
            };

            await file.WriteLineAsync(JsonSerializer.Serialize(exportRow, JsonOptions));
        }

        return new EmbeddingExportResult(rows.Count, fullPath);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
