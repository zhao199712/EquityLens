using System.Text.Json;
using EquityLens.Api.Services.Ai;

namespace EquityLens.Api.Services.Agents;

public sealed class LlmCriticReviewAgent : ICriticReviewAgent
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> AllowedSeverities = new(StringComparer.OrdinalIgnoreCase)
    {
        "None",
        "Low",
        "Medium",
        "High",
        "Critical"
    };
    private static readonly HashSet<string> AllowedFindingSeverities = new(StringComparer.OrdinalIgnoreCase)
    {
        "Low",
        "Medium",
        "High",
        "Critical"
    };
    private static readonly HashSet<string> AllowedCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "UnsupportedClaim",
        "WeakCitation",
        "MissingCitation",
        "InsufficientEvidence",
        "Contradiction",
        "Overclaim",
        "MissingAnswer",
        "General"
    };

    private readonly IChatCompletionService _chatCompletion;
    private readonly ILogger<LlmCriticReviewAgent> _logger;

    public LlmCriticReviewAgent(IChatCompletionService chatCompletion, ILogger<LlmCriticReviewAgent> logger)
    {
        _chatCompletion = chatCompletion;
        _logger = logger;
    }

    public async Task<CriticReviewResult> CritiqueAsync(CriticReviewInput input, CancellationToken cancellationToken = default)
    {
        var response = await _chatCompletion.CompleteAsync(
            new ChatCompletionRequest(
                BuildSystemPrompt(),
                BuildUserPrompt(input),
                Temperature: 0.1,
                MaxTokens: 4096,
                ResponseFormat: ChatResponseFormat.JsonObject),
            cancellationToken);

        if (string.IsNullOrWhiteSpace(response.Content))
        {
            throw new InvalidOperationException("LLM critic returned empty JSON content.");
        }

        CriticReviewResult? result;
        try
        {
            result = JsonSerializer.Deserialize<CriticReviewResult>(response.Content, SerializerOptions);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "LLM critic returned invalid JSON: {Content}", response.Content);
            throw new InvalidOperationException("LLM critic returned invalid JSON content.", exception);
        }

        if (result is null)
        {
            throw new InvalidOperationException("LLM critic returned empty JSON object.");
        }

        Validate(result, input.CitationCount);
        return result;
    }

    private static string BuildSystemPrompt() =>
        """
        You are a strict investment research answer critic. You must output valid json only. Do not output markdown, explanations, or code fences.

        Review whether the answer is grounded in the supplied citation/evidence checks and whether it overclaims beyond cited evidence.
        Use Traditional Chinese for every string value in the json output.
        Do not decide workflow routing. Do not output requiresRevision, requiresMoreEvidence, routeBackTo, or recommendedNextAction.

        Allowed overallSeverity values: None, Low, Medium, High, Critical.
        Allowed finding severity values: Low, Medium, High, Critical.
        Allowed finding category values: UnsupportedClaim, WeakCitation, MissingCitation, InsufficientEvidence, Contradiction, Overclaim, MissingAnswer, General.

        EXAMPLE JSON OUTPUT:
        {
          "summary": "No material grounding issues were found.",
          "overallSeverity": "None",
          "findings": [],
          "suggestedAnswerRevision": null
        }
        """;

    private static string BuildUserPrompt(CriticReviewInput input)
    {
        var evidenceFindingsJson = JsonSerializer.Serialize(input.EvidenceFindings, SerializerOptions);
        return $$"""
        Produce valid json for the CriticReviewResult schema.

        Ticker: {{input.Ticker ?? ""}}
        Question: {{input.Question ?? ""}}
        Answer:
        {{input.Answer ?? ""}}

        Source status: {{input.SourceStatus}}
        Citation count: {{input.CitationCount}}
        Candidate count: {{input.CandidateCount}}
        Deterministic evidence findings json:
        {{evidenceFindingsJson}}

        Required json schema:
        {
          "summary": "string",
          "overallSeverity": "None|Low|Medium|High|Critical",
          "findings": [
            {
              "severity": "Low|Medium|High|Critical",
              "category": "UnsupportedClaim|WeakCitation|MissingCitation|InsufficientEvidence|Contradiction|Overclaim|MissingAnswer|General",
              "message": "string",
              "relatedCitationIndexes": [1],
              "recommendation": "string"
            }
          ],
          "suggestedAnswerRevision": "string|null"
        }
        """;
    }

    private static void Validate(CriticReviewResult result, int citationCount)
    {
        if (string.IsNullOrWhiteSpace(result.Summary))
        {
            throw new InvalidOperationException("LLM critic result is missing summary.");
        }
        if (!AllowedSeverities.Contains(result.OverallSeverity))
        {
            throw new InvalidOperationException($"LLM critic result has invalid overallSeverity '{result.OverallSeverity}'.");
        }
        if (result.Findings is null)
        {
            throw new InvalidOperationException("LLM critic result is missing findings.");
        }
        if (result.Findings.Count == 0 && !string.Equals(result.OverallSeverity, "None", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("LLM critic result with no findings must use overallSeverity None.");
        }
        if (result.Findings.Count > 0 && string.Equals(result.OverallSeverity, "None", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("LLM critic result with findings cannot use overallSeverity None.");
        }

        foreach (var finding in result.Findings)
        {
            ValidateFinding(finding, citationCount);
        }
    }

    private static void ValidateFinding(CriticFinding finding, int citationCount)
    {
        if (!AllowedFindingSeverities.Contains(finding.Severity))
        {
            throw new InvalidOperationException($"LLM critic finding has invalid severity '{finding.Severity}'.");
        }
        if (!AllowedCategories.Contains(finding.Category))
        {
            throw new InvalidOperationException($"LLM critic finding has invalid category '{finding.Category}'.");
        }
        if (string.IsNullOrWhiteSpace(finding.Message))
        {
            throw new InvalidOperationException("LLM critic finding is missing message.");
        }
        if (string.IsNullOrWhiteSpace(finding.Recommendation))
        {
            throw new InvalidOperationException("LLM critic finding is missing recommendation.");
        }
        if (finding.RelatedCitationIndexes is null)
        {
            throw new InvalidOperationException("LLM critic finding is missing relatedCitationIndexes.");
        }
        foreach (var citationIndex in finding.RelatedCitationIndexes)
        {
            if (citationIndex < 1 || citationIndex > citationCount)
            {
                throw new InvalidOperationException($"LLM critic finding has out-of-range citation index {citationIndex}.");
            }
        }
    }
}
