using System.Text.Json;
using EquityLens.Api.Services.Ai;

namespace EquityLens.Api.Services.Agents;

public interface IDraftRevisionAgent
{
    Task<DraftRevisionResult> ReviseAsync(DraftRevisionInput input, CancellationToken cancellationToken = default);
}

public sealed record DraftRevisionInput(
    string? Ticker,
    string? Question,
    string? SourceAnswer,
    bool RequiresRevision,
    string CriticSummary,
    string OverallSeverity,
    IReadOnlyList<CriticFinding> Findings,
    string? SuggestedAnswerRevision,
    string? AppliedRecommendation);

public sealed record DraftRevisionResult(
    string RevisedAnswer,
    string RevisionSummary,
    string? AppliedRecommendation);

public sealed class DeterministicDraftRevisionAgent : IDraftRevisionAgent
{
    public const string FallbackRecommendation = "請補強引用支撐，並避免超出來源證據的推論。";

    public Task<DraftRevisionResult> ReviseAsync(DraftRevisionInput input, CancellationToken cancellationToken = default)
    {
        if (!input.RequiresRevision)
        {
            return Task.FromResult(new DraftRevisionResult(
                input.SourceAnswer ?? string.Empty,
                "CriticReview 未要求修訂，保留原回答。",
                null));
        }

        var appliedRecommendation = CreateAppliedRecommendation(input.SuggestedAnswerRevision);
        var revisedAnswer = CreateRevisedAnswer(input.SourceAnswer, appliedRecommendation);
        return Task.FromResult(new DraftRevisionResult(
            revisedAnswer,
            "已根據 CriticReview 建議產生 deterministic 修訂稿。",
            appliedRecommendation));
    }

    private static string CreateAppliedRecommendation(string? suggestedAnswerRevision) =>
        string.IsNullOrWhiteSpace(suggestedAnswerRevision)
            ? FallbackRecommendation
            : suggestedAnswerRevision;

    private static string CreateRevisedAnswer(string? sourceAnswer, string appliedRecommendation)
    {
        if (!string.Equals(appliedRecommendation, FallbackRecommendation, StringComparison.Ordinal))
        {
            return appliedRecommendation;
        }

        return string.IsNullOrWhiteSpace(sourceAnswer)
            ? "修訂稿待補：原回答為空，需先補充可引用證據。"
            : $"{sourceAnswer}\n\n修訂提醒：{appliedRecommendation}";
    }
}

public sealed class LlmDraftRevisionAgent : IDraftRevisionAgent
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IChatCompletionService _chatCompletion;
    private readonly ILogger<LlmDraftRevisionAgent> _logger;

    public LlmDraftRevisionAgent(IChatCompletionService chatCompletion, ILogger<LlmDraftRevisionAgent> logger)
    {
        _chatCompletion = chatCompletion;
        _logger = logger;
    }

    public async Task<DraftRevisionResult> ReviseAsync(DraftRevisionInput input, CancellationToken cancellationToken = default)
    {
        if (!input.RequiresRevision)
        {
            return await new DeterministicDraftRevisionAgent().ReviseAsync(input, cancellationToken);
        }

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
            throw new AgentNodeException("llm_draft_empty_content", AgentNodeErrorCategories.ValidationFailure, "LLM draft revision returned empty JSON content.");
        }

        DraftRevisionResult? result;
        try
        {
            result = JsonSerializer.Deserialize<DraftRevisionResult>(response.Content, SerializerOptions);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "LLM draft revision returned invalid JSON: {Content}", response.Content);
            throw new AgentNodeException("llm_draft_invalid_json", AgentNodeErrorCategories.ValidationFailure, "LLM draft revision returned invalid JSON content.", retryable: false, innerException: exception);
        }

        if (result is null)
        {
            throw new AgentNodeException("llm_draft_empty_object", AgentNodeErrorCategories.ValidationFailure, "LLM draft revision returned empty JSON object.");
        }

        Validate(result, input.RequiresRevision);
        return result;
    }

    private static string BuildSystemPrompt() =>
        """
        You are a professional Traditional Chinese investment research answer editor. You must output valid json only. Do not output markdown, explanations, or code fences.

        Revise the source answer according to the CriticReview findings. Preserve uncertainty when evidence is insufficient. Do not fabricate facts, dates, numbers, citations, or source claims.
        Use Traditional Chinese for every string value in the json output.

        EXAMPLE JSON OUTPUT:
        {
          "revisedAnswer": "目前提供的資料不足以回答此問題。",
          "revisionSummary": "已移除未受證據支持的推論，並保留資料不足結論。",
          "appliedRecommendation": "補強引用支撐並避免超出來源證據。"
        }
        """;

    private static string BuildUserPrompt(DraftRevisionInput input)
    {
        var findingsJson = JsonSerializer.Serialize(input.Findings, SerializerOptions);
        return $$"""
        Produce valid json for the DraftRevisionResult schema.

        Ticker: {{input.Ticker ?? ""}}
        Question: {{input.Question ?? ""}}
        Requires revision: {{input.RequiresRevision}}
        Critic summary: {{input.CriticSummary}}
        Overall severity: {{input.OverallSeverity}}
        Suggested answer revision: {{input.SuggestedAnswerRevision ?? ""}}
        Applied recommendation baseline: {{input.AppliedRecommendation ?? ""}}

        Source answer:
        {{input.SourceAnswer ?? ""}}

        Critic findings json:
        {{findingsJson}}

        Required json schema:
        {
          "revisedAnswer": "string",
          "revisionSummary": "string",
          "appliedRecommendation": "string"
        }
        """;
    }

    private static void Validate(DraftRevisionResult result, bool requiresRevision)
    {
        if (string.IsNullOrWhiteSpace(result.RevisedAnswer))
        {
            throw new AgentNodeException("llm_draft_missing_answer", AgentNodeErrorCategories.ValidationFailure, "LLM draft revision result is missing revisedAnswer.");
        }
        if (string.IsNullOrWhiteSpace(result.RevisionSummary))
        {
            throw new AgentNodeException("llm_draft_missing_summary", AgentNodeErrorCategories.ValidationFailure, "LLM draft revision result is missing revisionSummary.");
        }
        if (requiresRevision && string.IsNullOrWhiteSpace(result.AppliedRecommendation))
        {
            throw new AgentNodeException("llm_draft_missing_recommendation", AgentNodeErrorCategories.ValidationFailure, "LLM draft revision result is missing appliedRecommendation.");
        }
    }
}
