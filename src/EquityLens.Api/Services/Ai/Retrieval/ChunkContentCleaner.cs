using System.Text.RegularExpressions;

namespace EquityLens.Api.Services.Ai.Retrieval;

public sealed partial class ChunkContentCleaner : IChunkContentCleaner
{
    public string Clean(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var withoutMetadata = MetadataFieldRegex().Replace(content, " ");
        var withoutBoilerplate = BoilerplateRegex().Replace(withoutMetadata, " ");
        return string.Join(" ", withoutBoilerplate
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace("\t", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim())
            .Where(part => part.Length > 0))
            .Trim();
    }

    [GeneratedRegex(@"(?:Company:\s*\S+|Ticker:\s*\S+|Exchange:\s*\S+|DocumentType:\s*\S+|Source:\s*\S+|ConferenceDate:\s*\S+|Language:\s*\S+|Page:\s*\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MetadataFieldRegex();

    [GeneratedRegex(@"(?:UnleashInnovation©?\s*\d*\s*2026\s*TSMC,?\s*Ltd\s*TSMC\s*Property|UnleashInnovation©?|TSMC,?\s*Ltd|TSMC\s*Property|https?://www\.tsmc\.com|invest@tsmc\.com)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BoilerplateRegex();
}
