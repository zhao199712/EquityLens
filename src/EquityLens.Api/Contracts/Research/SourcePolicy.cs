using System.Text.Json;
using System.Text.Json.Serialization;

namespace EquityLens.Api.Contracts.Research;

[JsonConverter(typeof(SourcePolicyJsonConverter))]
public enum SourcePolicy
{
    LocalOnly,
    LocalThenWeb,
    LocalAndWeb,
    WebOnly
}

public sealed class SourcePolicyJsonConverter : JsonConverter<SourcePolicy>
{
    public override SourcePolicy Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            if (!string.IsNullOrWhiteSpace(value) && Enum.TryParse<SourcePolicy>(value, ignoreCase: true, out var mode) && Enum.IsDefined(mode))
            {
                return mode;
            }
        }

        if (reader.TokenType == JsonTokenType.Number)
        {
            throw new JsonException("sourcePolicy must be a string, not a number.");
        }

        return SourcePolicy.LocalOnly;  // default for null
    }

    public override void Write(Utf8JsonWriter writer, SourcePolicy value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
