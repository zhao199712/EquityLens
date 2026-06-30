using System.Text.Json;

namespace EquityLens.Api.Services.AgentRuns;

/// <summary>
/// Finalizes the critic report by computing overall severity and generating
/// the final structured output from the blackboard's criticFindings.
/// </summary>
public sealed class FinalizeCriticReportNode : IWorkflowNode
{
    public string NodeType => "FinalizeCriticReport";

    public Task<NodeExecutionResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken ct)
    {
        var blackboard = JsonSerializer.Deserialize<Dictionary<string, object?>>(context.BlackboardJson) ?? [];

        var answer = blackboard.GetValueOrDefault("answer")?.ToString() ?? "(no answer)";
        var criticFindingsRaw = blackboard.GetValueOrDefault("criticFindings");

        // Parse findings
        var findings = new List<Dictionary<string, object?>>();
        if (criticFindingsRaw is JsonElement findingsElement && findingsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in findingsElement.EnumerateArray())
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(item);
                if (dict is not null) findings.Add(dict);
            }
        }

        // Compute overall severity
        var overallSeverity = ComputeOverallSeverity(findings);

        // Generate summary
        var summary = GenerateSummary(findings, overallSeverity);

        // Generate suggested revision
        var suggestedRevision = GenerateSuggestedRevision(findings, answer);

        var finalOutput = new
        {
            summary,
            overallSeverity,
            findings = findings.Select(f => new
            {
                severity = f.GetValueOrDefault("severity")?.ToString() ?? "Unknown",
                category = f.GetValueOrDefault("category")?.ToString() ?? "Unknown",
                message = f.GetValueOrDefault("message")?.ToString() ?? "",
                recommendation = f.GetValueOrDefault("recommendation")?.ToString() ?? ""
            }).ToArray(),
            suggestedAnswerRevision = suggestedRevision
        };

        var output = new { finalOutput };
        return Task.FromResult(new NodeExecutionResult(true, OutputJson: JsonSerializer.Serialize(output)));
    }

    private static string ComputeOverallSeverity(List<Dictionary<string, object?>> findings)
    {
        if (findings.Count == 0) return "Low";

        var severities = findings
            .Select(f => f.GetValueOrDefault("severity")?.ToString())
            .Where(s => s is not null)
            .ToList();

        if (severities.Any(s => s == "Critical")) return "Critical";
        if (severities.Any(s => s == "High")) return "High";
        if (severities.Any(s => s == "Medium")) return "Medium";
        return "Low";
    }

    private static string GenerateSummary(List<Dictionary<string, object?>> findings, string overallSeverity)
    {
        if (findings.Count == 0)
            return "回答品質良好，未發現明顯問題。";

        var categories = findings
            .Select(f => f.GetValueOrDefault("category")?.ToString())
            .Where(c => c is not null)
            .Distinct()
            .ToList();

        return $"回答共發現 {findings.Count} 個問題（整體嚴重度：{overallSeverity}），" +
               $"主要類別包括：{string.Join("、", categories)}。";
    }

    private static string GenerateSuggestedRevision(List<Dictionary<string, object?>> findings, string answer)
    {
        if (findings.Count == 0)
            return "回答品質良好，無需修改。";

        var recommendations = findings
            .Select(f => f.GetValueOrDefault("recommendation")?.ToString())
            .Where(r => !string.IsNullOrEmpty(r))
            .ToList();

        return recommendations.Count > 0
            ? $"建議改進：{string.Join("；", recommendations)}"
            : "建議人工審查後決定是否修改。";
    }
}
