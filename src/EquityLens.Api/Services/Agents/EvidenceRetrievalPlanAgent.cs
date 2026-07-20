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
    private const string SystemPrompt = """
        You are an evidence-retrieval planner for Taiwan public-equity research. Return one JSON object only; never return markdown.
        Your authority is limited to retrieval parameters. Never output workflow nodes, edges, tools, routing decisions, answers, or unsupported facts.

        Plan 1-3 focused searches that close the supplied unresolved claims. Do not merely paraphrase the original question.
        Prefer first-party evidence: AnnualReport for audited disclosures and risk factors; EarningsPresentation for management commentary, guidance, and recent operating context.
        Each search must target one explicit unresolved claim, use a materially different query from previousQueries, and explain why the selected source can resolve that gap.

        Allowed values:
        - documentType: "AnnualReport", "EarningsPresentation", or null for mixed local retrieval
        - sourceRole: "Primary" or "Supporting"
        - topK: integer 1-8
        - freshness: null, "day", "week", "month", or "year"; use it only when recent evidence is material

        Required JSON shape:
        {"searches":[{"documentType":"AnnualReport","sourceRole":"Primary","query":"specific search query","topK":6,"reason":"why this source/query closes the gap","targetClaim":"exact unresolved claim being addressed","freshness":null}]}
        """;
    public async Task<EvidenceRetrievalPlanningResult> PlanAsync(EvidenceRetrievalPlanningInput input, CancellationToken cancellationToken = default)
    {
        string? reason = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var result = await chat.CompleteAsync(new ChatCompletionRequest(
                    SystemPrompt,
                    JsonSerializer.Serialize(new { task = "Close evidence gaps without changing workflow topology", input.Question, input.UnresolvedClaims, input.Findings, input.PreviousQueries, input.Iteration, budget = new { searches = 3, maxTopKPerSearch = 8, maxIterations = EvidenceRemediationWorkflow.MaxIterations }, validationError = reason }, Json),
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
            x.TryGetProperty("reason", out var r) ? r.GetString() ?? string.Empty : string.Empty,
            x.TryGetProperty("targetClaim", out var t) ? t.GetString() : null,
            x.TryGetProperty("freshness", out var f) && f.ValueKind != JsonValueKind.Null ? f.GetString() : null)).ToList();
        if (searches.Count is < 1 or > 3) throw new InvalidOperationException("Planner must return 1-3 searches.");
        if (searches.Any(x => string.IsNullOrWhiteSpace(x.Query) || string.IsNullOrWhiteSpace(x.Reason) || string.IsNullOrWhiteSpace(x.TargetClaim) || x.TopK is < 1 or > 8)) throw new InvalidOperationException("Planner query, targetClaim, reason, or topK is invalid.");
        if (searches.Any(x => x.DocumentType is not (null or "AnnualReport" or "EarningsPresentation"))) throw new InvalidOperationException("Planner documentType is not allowed.");
        if (searches.Any(x => x.SourceRole is not ("Primary" or "Supporting"))) throw new InvalidOperationException("Planner sourceRole is not allowed.");
        if (searches.Any(x => x.Freshness is not (null or "day" or "week" or "month" or "year"))) throw new InvalidOperationException("Planner freshness is not allowed.");
        if (searches.Select(x => x.Query.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != searches.Count || searches.Any(x => previous.Contains(x.Query, StringComparer.OrdinalIgnoreCase))) throw new InvalidOperationException("Planner returned a duplicate query.");
        return new("Auto", searches);
    }
}
