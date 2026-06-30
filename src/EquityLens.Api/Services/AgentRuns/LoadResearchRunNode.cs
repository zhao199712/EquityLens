using System.Text.Json;
using EquityLens.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.AgentRuns;

/// <summary>
/// Loads a persisted research run's data into the blackboard.
/// Expects `researchRunId` in the blackboard, populates `researchRun`, `answer`, `citations`.
/// </summary>
public sealed class LoadResearchRunNode : IWorkflowNode
{
    public string NodeType => "LoadResearchRun";

    public async Task<NodeExecutionResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken ct)
    {
        var blackboard = JsonSerializer.Deserialize<Dictionary<string, object?>>(context.BlackboardJson) ?? [];

        // Extract researchRunId from blackboard
        if (!blackboard.TryGetValue("researchRunId", out var researchRunIdObj) || researchRunIdObj is null)
            return new NodeExecutionResult(false, ErrorMessage: "researchRunId is missing from blackboard.");

        var researchRunIdStr = researchRunIdObj.ToString();
        if (!Guid.TryParse(researchRunIdStr, out var researchRunId))
            return new NodeExecutionResult(false, ErrorMessage: $"Invalid researchRunId: {researchRunIdStr}");

        var db = context.ServiceProvider.GetRequiredService<EquityLensDbContext>();

        // Try to find a persisted AgentRun with this ID
        var researchRun = await db.AgentRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == researchRunId, ct);

        if (researchRun is null)
            return new NodeExecutionResult(false, ErrorMessage: $"AgentRun '{researchRunId}' not found.");

        // Try to parse the output as a research response
        Dictionary<string, object?>? researchData = null;
        if (!string.IsNullOrEmpty(researchRun.OutputJson))
        {
            researchData = JsonSerializer.Deserialize<Dictionary<string, object?>>(
                JsonDocument.Parse(researchRun.OutputJson).RootElement);
        }
        else if (!string.IsNullOrEmpty(researchRun.InputJson))
        {
            // Fallback: use input data
            researchData = JsonSerializer.Deserialize<Dictionary<string, object?>>(
                JsonDocument.Parse(researchRun.InputJson).RootElement);
        }

        // Build output: populate blackboard fields
        var output = new Dictionary<string, object?>
        {
            ["researchRun"] = new
            {
                id = researchRun.Id.ToString(),
                workflowType = researchRun.WorkflowType,
                status = researchRun.Status,
                createdAtUtc = researchRun.CreatedAtUtc.ToString("o")
            },
            ["answer"] = researchData?.GetValueOrDefault("answer")?.ToString() ?? "(no answer available)",
            ["citations"] = researchData?.GetValueOrDefault("citations") ?? Array.Empty<object>(),
            ["steps"] = researchData?.GetValueOrDefault("steps") ?? Array.Empty<object>(),
            ["candidates"] = researchData?.GetValueOrDefault("candidates") ?? Array.Empty<object>()
        };

        // Try to extract ticker from input
        if (researchData?.TryGetValue("ticker", out var ticker) == true && ticker is not null)
            output["ticker"] = ticker.ToString();

        // Try to extract question from input
        if (researchData?.TryGetValue("question", out var question) == true && question is not null)
            output["question"] = question.ToString();

        return new NodeExecutionResult(true, OutputJson: JsonSerializer.Serialize(output));
    }
}
