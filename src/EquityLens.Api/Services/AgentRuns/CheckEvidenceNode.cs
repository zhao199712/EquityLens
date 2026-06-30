using System.Text.Json;

namespace EquityLens.Api.Services.AgentRuns;

/// <summary>
/// Checks citation coverage and evidence quality of the research answer.
/// Reads `answer` and `citations` from blackboard, produces `evidenceChecks`.
/// </summary>
public sealed class CheckEvidenceNode : IWorkflowNode
{
    public string NodeType => "CheckEvidence";

    public Task<NodeExecutionResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken ct)
    {
        var blackboard = JsonSerializer.Deserialize<Dictionary<string, object?>>(context.BlackboardJson) ?? [];

        var answer = blackboard.GetValueOrDefault("answer")?.ToString() ?? "";
        var citationsRaw = blackboard.GetValueOrDefault("citations");

        // Parse citations
        var citations = new List<Dictionary<string, object?>>();
        if (citationsRaw is JsonElement citationsElement && citationsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in citationsElement.EnumerateArray())
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(item);
                if (dict is not null) citations.Add(dict);
            }
        }

        var citationCount = citations.Count;

        // Check for unsupported claims: sentences in answer that reference specific data
        // but have no matching citation
        var unsupportedClaims = new List<object>();
        var weakEvidenceClaims = new List<object>();
        var missingCitationClaims = new List<object>();

        if (string.IsNullOrWhiteSpace(answer))
        {
            missingCitationClaims.Add(new { claim = "Answer is empty", reason = "No answer text to analyze." });
        }
        else if (citationCount == 0)
        {
            unsupportedClaims.Add(new
            {
                claim = "Answer contains no citations",
                reason = "Entire answer is unsupported by any citation."
            });
        }
        else
        {
            // Simple heuristic: split answer into sentences, check if each has a citation reference
            var sentences = answer.Split(new[] { '。', '，', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var sentence in sentences)
            {
                var trimmed = sentence.Trim();
                if (trimmed.Length < 5) continue;

                // Check if sentence references numbers or specific data
                var hasNumber = trimmed.Any(char.IsDigit);
                var hasCitationRef = trimmed.Contains("[") || citations.Any(c =>
                {
                    var quote = c.GetValueOrDefault("quoteText")?.ToString();
                    return !string.IsNullOrEmpty(quote) && trimmed.Contains(quote[..Math.Min(20, quote.Length)]);
                });

                if (hasNumber && !hasCitationRef)
                {
                    weakEvidenceClaims.Add(new
                    {
                        claim = trimmed,
                        reason = "Contains numerical data without citation reference."
                    });
                }
            }
        }

        var output = new
        {
            citationCount,
            missingCitationClaims = missingCitationClaims.ToArray(),
            weakEvidenceClaims = weakEvidenceClaims.ToArray(),
            unsupportedClaims = unsupportedClaims.ToArray()
        };

        return Task.FromResult(new NodeExecutionResult(true, OutputJson: JsonSerializer.Serialize(new { evidenceChecks = output })));
    }
}
