using System.Text.RegularExpressions;

namespace EquityLens.Api.Services.Ai.Retrieval;

public sealed class CitationValidator : ICitationValidator
{
    private static readonly Regex CitationPattern = new(@"\[(\d+)\]", RegexOptions.Compiled);

    public CitationValidationResult Validate(string answer, int maxValidIndex)
    {
        if (maxValidIndex <= 0 || string.IsNullOrWhiteSpace(answer))
        {
            return new CitationValidationResult(true, [], answer ?? string.Empty);
        }

        var invalidIndices = new List<int>();

        foreach (Match match in CitationPattern.Matches(answer))
        {
            var index = int.Parse(match.Groups[1].Value);
            if (index < 1 || index > maxValidIndex)
            {
                invalidIndices.Add(index);
            }
        }

        if (invalidIndices.Count == 0)
        {
            return new CitationValidationResult(true, [], answer);
        }

        var invalidSet = new HashSet<int>(invalidIndices);
        var sanitized = CitationPattern.Replace(answer, match =>
        {
            var index = int.Parse(match.Groups[1].Value);
            return invalidSet.Contains(index) ? string.Empty : match.Value;
        });

        return new CitationValidationResult(false, invalidIndices.Distinct().OrderBy(i => i).ToList(), sanitized);
    }
}
