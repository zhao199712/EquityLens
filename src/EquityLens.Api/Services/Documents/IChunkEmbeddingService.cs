namespace EquityLens.Api.Services.Documents;

public sealed record ChunkEmbeddingResult(int Total, int Succeeded, int Failed, int Skipped);

public interface IChunkEmbeddingService
{
    Task<ChunkEmbeddingResult> EmbedMissingChunksAsync(CancellationToken cancellationToken = default);
}
