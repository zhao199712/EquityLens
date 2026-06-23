using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace EquityLens.Api.Services.Documents;

public sealed class ChunkEmbeddingService : IChunkEmbeddingService
{
    private const int BatchSize = 64;

    private readonly EquityLensDbContext _dbContext;
    private readonly IEmbeddingService _embeddingService;

    public ChunkEmbeddingService(EquityLensDbContext dbContext, IEmbeddingService embeddingService)
    {
        _dbContext = dbContext;
        _embeddingService = embeddingService;
    }

    public async Task<ChunkEmbeddingResult> EmbedMissingChunksAsync(CancellationToken cancellationToken = default)
    {
        var total = await _dbContext.DocumentChunks
            .Where(c => c.Embedding == null)
            .CountAsync(cancellationToken);

        var succeeded = 0;
        var failed = 0;
        var skipped = 0;

        while (true)
        {
            var chunks = await _dbContext.DocumentChunks
                .Include(c => c.Embedding)
                .Where(c => c.Embedding == null)
                .OrderBy(c => c.DocumentId)
                .ThenBy(c => c.ChunkIndex)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (chunks.Count == 0)
                break;

            try
            {
                var chunksToEmbed = chunks.Where(c => !IsNoiseChunk(c.Content)).ToList();
                skipped += chunks.Count - chunksToEmbed.Count;

                if (chunksToEmbed.Count > 0)
                {
                    var embeddings = await _embeddingService.CreateEmbeddingsAsync(
                        chunksToEmbed.Select(c => c.Content).ToList(),
                        cancellationToken);

                    for (var i = 0; i < chunksToEmbed.Count; i++)
                    {
                        _dbContext.DocumentEmbeddings.Add(new DocumentEmbedding
                        {
                            Id = Guid.NewGuid(),
                            DocumentChunkId = chunksToEmbed[i].Id,
                            Embedding = new Vector(embeddings[i]),
                            EmbeddingModel = _embeddingService.Model,
                            Dimensions = _embeddingService.Dimensions,
                            CreatedAtUtc = DateTime.UtcNow
                        });
                    }
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                succeeded += chunksToEmbed.Count;
                Console.WriteLine($"  ✓ embedded {succeeded}/{total} (skipped {skipped} noise)");
            }
            catch (Exception ex)
            {
                failed += chunks.Count;
                Console.WriteLine($"  ✗ batch failed ({chunks.Count} chunks): {ex.Message}");
                break;
            }
        }

        return new ChunkEmbeddingResult(total, succeeded, failed, Math.Max(0, total - succeeded - failed - skipped), skipped);
    }

    private static bool IsNoiseChunk(string content)
    {
        var normalized = content.Replace("\n", " ").Replace("\t", " ").Replace(" ", string.Empty);
        return ContainsAny(normalized, ["SafeHarborNotice", "forward-lookingstatements", "Agenda", "會議議程"]);
    }

    private static bool ContainsAny(string value, IReadOnlyList<string> candidates)
    {
        return candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
    }
}
