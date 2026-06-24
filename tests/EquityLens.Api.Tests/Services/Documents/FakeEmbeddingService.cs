using EquityLens.Api.Services.Documents;

namespace EquityLens.Api.Tests.Services.Documents;

public sealed class FakeEmbeddingService : IEmbeddingService
{
    public string Model => "fake-embedding";
    public int Dimensions => 1536;

    public Task<IReadOnlyList<float[]>> CreateEmbeddingsAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default)
    {
        var embeddings = inputs.Select(input => CreateDeterministicVector(input, Dimensions)).ToList();
        return Task.FromResult<IReadOnlyList<float[]>>(embeddings);
    }

    private static float[] CreateDeterministicVector(string input, int dimensions)
    {
        var random = new Random(input.GetHashCode(StringComparison.Ordinal));
        var values = new float[dimensions];
        for (var i = 0; i < dimensions; i++)
        {
            values[i] = (float)(random.NextDouble() * 2 - 1);
        }

        var magnitude = MathF.Sqrt(values.Sum(v => v * v));
        if (magnitude > 0)
        {
            for (var i = 0; i < dimensions; i++)
            {
                values[i] /= magnitude;
            }
        }

        return values;
    }
}
