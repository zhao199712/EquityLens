namespace EquityLens.Api.Services.Documents;

public sealed record ConferenceChunkingResult(int Total, int Succeeded, int Failed, int Skipped, int ChunksCreated);

public interface IConferenceChunkingService
{
    Task<ConferenceChunkingResult> ChunkConferencesAsync(CancellationToken cancellationToken = default);
}
