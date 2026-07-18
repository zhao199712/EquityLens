using System.Text.Json;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Services.Ai;
using EquityLens.Api.Services.Ai.Retrieval;

namespace EquityLens.Api.Services.Agents;

public sealed record EvidenceRetrievalPlanningInput(string Question, IReadOnlyList<string> UnresolvedClaims, IReadOnlyList<CriticFinding> Findings, IReadOnlyList<string> PreviousQueries, int Iteration);
public sealed record EvidenceRetrievalPlanningResult(ResearchRetrievalStrategy Plan, string Mode, string Model, int PromptTokens, int CompletionTokens, string? FallbackReason);
public interface IEvidenceRetrievalPlanAgent { Task<EvidenceRetrievalPlanningResult> PlanAsync(EvidenceRetrievalPlanningInput input, CancellationToken cancellationToken = default); }

public sealed class EvidenceRetrievalPlanAgent(IChatCompletionService chat, IRetrievalPlanner fallback) : IEvidenceRetrievalPlanAgent
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<EvidenceRetrievalPlanningResult> PlanAsync(EvidenceRetrievalPlanningInput input, CancellationToken cancellationToken = default)
    {
        string? reason = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var result = await chat.CompleteAsync(new ChatCompletionRequest(
                    "Return JSON only. Plan evidence retrieval; never output workflow nodes or edges. searches must contain 1-3 items with documentType, sourceRole, query, topK, reason. topK must be 1-8.",
                    JsonSerializer.Serialize(new { input.Question, input.UnresolvedClaims, input.Findings, input.PreviousQueries, input.Iteration, validationError = reason }, Json),
                    0.1, 1200, ChatResponseFormat.JsonObject), cancellationToken);
                var plan = ParseAndValidate(result.Content, input.PreviousQueries);
                return new(plan, attempt == 0 ? "Llm" : "LlmRepair", result.Model, result.PromptTokens, result.CompletionTokens, null);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                reason = ex.Message;
            }
        }
        var query = input.UnresolvedClaims.Count > 0 ? $"{input.Question}\nUnresolved claims: {string.Join("; ", input.UnresolvedClaims)}" : input.Question;
        return new(fallback.BuildPlan(query, null, null, 5), "DeterministicFallback", chat.Model, 0, 0, reason);
    }

    private static ResearchRetrievalStrategy ParseAndValidate(string content, IReadOnlyList<string> previous)
    {
        using var doc = JsonDocument.Parse(content); var searches = doc.RootElement.GetProperty("searches").EnumerateArray().Select(x => new ResearchRetrievalSearch(
            x.TryGetProperty("documentType", out var d) && d.ValueKind != JsonValueKind.Null ? d.GetString() : null,
            x.TryGetProperty("sourceRole", out var s) ? s.GetString() ?? "Primary" : "Primary",
            x.GetProperty("query").GetString() ?? string.Empty,
            x.GetProperty("topK").GetInt32(),
            x.TryGetProperty("reason", out var r) ? r.GetString() ?? string.Empty : string.Empty)).ToList();
        if (searches.Count is < 1 or > 3) throw new InvalidOperationException("Planner must return 1-3 searches.");
        if (searches.Any(x => string.IsNullOrWhiteSpace(x.Query) || x.TopK is < 1 or > 8)) throw new InvalidOperationException("Planner query or topK is invalid.");
        if (searches.Select(x => x.Query.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != searches.Count || searches.Any(x => previous.Contains(x.Query, StringComparer.OrdinalIgnoreCase))) throw new InvalidOperationException("Planner returned a duplicate query.");
        return new("Auto", searches);
    }
}
