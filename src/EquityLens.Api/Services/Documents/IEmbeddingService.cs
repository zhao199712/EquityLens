namespace EquityLens.Api.Services.Documents;

public interface IEmbeddingService
{
    string Model { get; }
    int Dimensions { get; }
    Task<IReadOnlyList<float[]>> CreateEmbeddingsAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken = default);
}
