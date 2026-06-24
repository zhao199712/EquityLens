using System.Text.Json;
using System.Text.Json.Serialization;

namespace EquityLens.Api.Contracts.Research;

[JsonConverter(typeof(RetrievalModeJsonConverter))]
public enum RetrievalMode
{
    Auto,
    ConferenceOnly,
    AnnualReportOnly,
    AllDocuments
}

public sealed class RetrievalModeJsonConverter : JsonConverter<RetrievalMode>
{
    public override RetrievalMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            if (!string.IsNullOrWhiteSpace(value) && Enum.TryParse<RetrievalMode>(value, ignoreCase: true, out var mode) && Enum.IsDefined(mode))
            {
                return mode;
            }

            throw new JsonException($"Invalid retrievalMode '{value}'. Allowed values: {string.Join(", ", Enum.GetNames<RetrievalMode>())}.");
        }

        if (reader.TokenType == JsonTokenType.Number)
        {
            throw new JsonException("retrievalMode must be a string, not a number.");
        }

        throw new JsonException("retrievalMode must be a string.");
    }

    public override void Write(Utf8JsonWriter writer, RetrievalMode value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
