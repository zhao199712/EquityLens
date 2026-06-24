using System.Diagnostics;
using EquityLens.Api.Observability;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Services.Ai.Retrieval;

public sealed class AnswerGenerator : IAnswerGenerator
{
    private readonly IChatCompletionService _chatCompletion;
    private readonly ICitationValidator _citationValidator;
    private readonly RetrievalOptions _options;
    private readonly ILogger<AnswerGenerator> _logger;

    public string Provider => _chatCompletion.Provider;
    public string Model => _chatCompletion.Model;

    public AnswerGenerator(
        IChatCompletionService chatCompletion,
        ICitationValidator citationValidator,
        IOptions<RetrievalOptions> options,
        ILogger<AnswerGenerator> logger)
    {
        _chatCompletion = chatCompletion;
        _citationValidator = citationValidator;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AnswerGenerationResult> GenerateAsync(
        string question,
        string context,
        string? retrievalNote,
        double temperature,
        int maxValidCitationIndex,
        CancellationToken cancellationToken = default)
    {
        var systemPrompt = BuildSystemPrompt(retrievalNote);
        var userPrompt = BuildUserPrompt(question, context, retrievalNote);

        using var llmActivity = EquityLensTelemetry.ActivitySource.StartActivity("llm.complete");
        llmActivity?.SetTag("llm.provider", _chatCompletion.Provider);
        llmActivity?.SetTag("llm.model", _chatCompletion.Model);

        ChatCompletionResult chatResult;
        try
        {
            chatResult = await _chatCompletion.CompleteAsync(
                new ChatCompletionRequest(SystemPrompt: systemPrompt, UserPrompt: userPrompt, Temperature: temperature),
                cancellationToken);
            llmActivity?.SetTag("llm.prompt_tokens", chatResult.PromptTokens);
            llmActivity?.SetTag("llm.completion_tokens", chatResult.CompletionTokens);
            llmActivity?.SetStatus(ActivityStatusCode.Ok);

            RecordTokens(chatResult, "prompt");
            RecordTokens(chatResult, "completion");
        }
        catch (Exception exception)
        {
            EquityLensTelemetry.MarkError(llmActivity, exception);
            throw;
        }

        var citationValidation = _citationValidator.Validate(chatResult.Content, maxValidCitationIndex);
        var citationValidationFailed = !citationValidation.IsValid;
        var retryCount = 0;
        var invalidCitationIndices = citationValidation.InvalidIndices;

        while (citationValidationFailed && retryCount < _options.MaxCitationRetries)
        {
            retryCount++;
            _logger.LogInformation(
                "Citation validation failed with invalid indices {InvalidIndices}; retry {RetryCount} of {MaxRetries}",
                string.Join(", ", invalidCitationIndices),
                retryCount,
                _options.MaxCitationRetries);

            var retryUserPrompt = BuildCitationRetryUserPrompt(
                question,
                context,
                chatResult.Content,
                invalidCitationIndices,
                retrievalNote);

            using var retryActivity = EquityLensTelemetry.ActivitySource.StartActivity("llm.complete.retry");
            retryActivity?.SetTag("llm.provider", _chatCompletion.Provider);
            retryActivity?.SetTag("llm.model", _chatCompletion.Model);
            retryActivity?.SetTag("citation.retry", retryCount);

            try
            {
                var retryResult = await _chatCompletion.CompleteAsync(
                    new ChatCompletionRequest(SystemPrompt: systemPrompt, UserPrompt: retryUserPrompt, Temperature: temperature),
                    cancellationToken);

                RecordTokens(retryResult, "prompt");
                RecordTokens(retryResult, "completion");

                chatResult = retryResult;
                citationValidation = _citationValidator.Validate(chatResult.Content, maxValidCitationIndex);
                citationValidationFailed = !citationValidation.IsValid;
                invalidCitationIndices = citationValidation.InvalidIndices;
                retryActivity?.SetStatus(ActivityStatusCode.Ok);
            }
            catch (Exception exception)
            {
                EquityLensTelemetry.MarkError(retryActivity, exception);
                throw;
            }
        }

        var finalAnswer = chatResult.Content;
        if (citationValidationFailed)
        {
            _logger.LogWarning(
                "Citation validation still failed after {RetryCount} retries; removing invalid citation markers {InvalidIndices}",
                retryCount,
                string.Join(", ", invalidCitationIndices));
            finalAnswer = citationValidation.SanitizedAnswer;
        }

        return new AnswerGenerationResult(
            finalAnswer,
            chatResult.Model,
            chatResult.PromptTokens,
            chatResult.CompletionTokens,
            retryCount,
            citationValidationFailed,
            invalidCitationIndices);
    }

    private void RecordTokens(ChatCompletionResult result, string direction)
    {
        var tags = new TagList
        {
            { "direction", direction },
            { "provider", _chatCompletion.Provider },
            { "model", result.Model }
        };
        var tokens = direction == "prompt" ? result.PromptTokens : result.CompletionTokens;
        EquityLensTelemetry.LlmTokens.Add(tokens, tags);
    }

    private static string BuildSystemPrompt(string? retrievalNote)
    {
        var retrievalInstruction = string.IsNullOrWhiteSpace(retrievalNote)
            ? ""
            : $"""

Critical retrieval status:
- {retrievalNote}
- You must explicitly mention this retrieval status in the answer before using Supporting-source disclosures.
- Do not say that conference materials were not searched. Say that conference materials were searched but no explicit risk-discussion excerpt was selected.
""";

        return $"""
You are a professional investment research assistant. Your task is to answer questions based solely on the provided document excerpts.

Rules:
1. Only answer using information from the provided documents.
2. Primary sources are direct evidence for the user's question. Supporting sources provide background or formal disclosure only.
3. Do not attribute Supporting-source facts to Primary sources.
4. If Primary sources lack details but Supporting sources contain relevant information, answer in two sections: "直接來源內容" and "補充來源揭露".
5. If all provided documents do not contain enough information to answer, clearly state "目前提供的資料不足以回答此問題。"
6. Use Traditional Chinese (繁體中文) for all responses.
7. After each important fact or conclusion, cite the source document using the bracket notation [1], [2], etc.
8. Use a neutral, objective, analytical tone.
9. Do not fabricate numbers, dates, or events not present in the provided text.
10. Choose evidence type according to the question intent. Financial statement table pages can support financial metric questions, but financial statement, outlook, or guidance pages should not be used as risk evidence unless they contain explicit risk discussion. Agenda pages and cover pages should not be used as substantive evidence.
11. If multiple documents provide related information, synthesize them while preserving source attribution.
12. For risk questions, never describe Supporting annual-report disclosures as risks mentioned by conference materials.
{retrievalInstruction}
""";
    }

    private static string BuildUserPrompt(string question, string context, string? retrievalNote)
    {
        var noteSection = string.IsNullOrWhiteSpace(retrievalNote)
            ? ""
            : $"""
重要檢索狀態：
{retrievalNote}
回答時必須先說明此狀態；若引用 Supporting 年報資料，請明確標示為年報補充揭露，不可說成法說會直接提到。

""";

        return $"""
以下是與問題相關的文件內容：

{noteSection}
{context}

===

問題：{question}

請根據以上文件回答。如果文件中沒有相關資訊，請明確說明。回答風險題時，請先區分「直接來源內容」與「補充來源揭露」。
""";
    }

    private static string BuildCitationRetryUserPrompt(
        string question,
        string context,
        string previousAnswer,
        IReadOnlyList<int> invalidIndices,
        string? retrievalNote)
    {
        var noteSection = string.IsNullOrWhiteSpace(retrievalNote)
            ? ""
            : $"""
重要檢索狀態：
{retrievalNote}
回答時必須先說明此狀態；若引用 Supporting 年報資料，請明確標示為年報補充揭露，不可說成法說會直接提到。

""";

        var invalidList = string.Join(", ", invalidIndices.Select(i => $"[{i}]"));

        return $"""
以下是與問題相關的文件內容。每個文件片段前皆有編號 [1]、[2]...，請只使用這些編號來引用：

{noteSection}
{context}

===

問題：{question}

你剛才的回答引用了不存在的編號 {invalidList}。請根據以上文件重新回答，並確保只使用編號 [1] 到 [{invalidIndices.Max()}] 之間的引用。如果文件中沒有相關資訊，請明確說明。
""";
    }
}
