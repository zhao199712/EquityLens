using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EquityLens.Api.Services.Agents;

public sealed record TypeSafeDecisionResponse(string Model, int InputTokens, JsonElement Answers)
{
    public double Noul(string key)
    {
        var answer = Answers.GetProperty(key);
        if (answer.GetProperty("type").GetString() != "noul") throw new JsonException("Unexpected Jev answer type.");
        var value = answer.GetProperty("noul").GetDouble();
        return double.IsFinite(value) && value is >= 0 and <= 1
            ? value : throw new JsonException("Jev returned a probability outside [0, 1].");
    }

    public (string Value, double Probability, double Confidence) Choice(string key, IReadOnlySet<string> allowed)
    {
        var answer = Answers.GetProperty(key);
        if (answer.GetProperty("type").GetString() != "choice") throw new JsonException("Unexpected Jev answer type.");
        var choice = answer.GetProperty("choice").GetString() ?? throw new JsonException("Jev choice is missing.");
        if (!allowed.Contains(choice)) throw new JsonException("Jev returned an unknown choice.");
        var probability = answer.GetProperty("probabilities").GetProperty(choice).GetDouble();
        var confidence = answer.GetProperty("confidence").GetDouble();
        if (!double.IsFinite(probability) || probability is < 0 or > 1 || !double.IsFinite(confidence) || confidence is < 0 or > 1)
            throw new JsonException("Jev returned an invalid choice probability.");
        return (choice, probability, confidence);
    }
}

public sealed class TypeSafeDecisionClient(HttpClient http, IConfiguration configuration)
{
    public async Task<TypeSafeDecisionResponse> EvaluateAsync(
        string model, object state, IReadOnlyDictionary<string, object> questions, CancellationToken cancellationToken)
    {
        var apiKey = configuration["TYPESAFE_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("Jev API key is not configured.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/systemone")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { model, state, questions }), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Jev returned HTTP {(int)response.StatusCode}.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = json.RootElement;
        return new TypeSafeDecisionResponse(
            root.GetProperty("model").GetString() ?? throw new JsonException("Jev model is missing."),
            root.GetProperty("usage").GetProperty("input_tokens").GetInt32(),
            root.GetProperty("answers").Clone());
    }

    public static object Noul(string instructions) => new { type = "noul", instructions };
    public static object Choice(string instructions, IReadOnlyDictionary<string, string> criteria) => new { type = "choice", instructions, criteria };
}
