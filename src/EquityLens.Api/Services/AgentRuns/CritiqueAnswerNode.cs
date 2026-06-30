using System.Text.Json;
using EquityLens.Api.Services.Ai;
using Microsoft.Extensions.DependencyInjection;

namespace EquityLens.Api.Services.AgentRuns;

/// <summary>
/// Uses LLM to critique the research answer for quality, citation coverage,
/// unsupported claims, contradictions, and overconfident language.
/// Produces `criticFindings` on the blackboard.
/// </summary>
public sealed class CritiqueAnswerNode : IWorkflowNode
{
    public string NodeType => "CritiqueAnswer";

    public async Task<NodeExecutionResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken ct)
    {
        var blackboard = JsonSerializer.Deserialize<Dictionary<string, object?>>(context.BlackboardJson) ?? [];

        var answer = blackboard.GetValueOrDefault("answer")?.ToString() ?? "(no answer)";
        var citationsRaw = blackboard.GetValueOrDefault("citations");
        var evidenceChecksRaw = blackboard.GetValueOrDefault("evidenceChecks");

        // Format citations for the prompt
        var citationsText = FormatCitations(citationsRaw);
        var evidenceChecksText = evidenceChecksRaw?.ToString() ?? "(none)";

        var systemPrompt = """
            你是一位嚴謹的金融研究品質審核員（Critic Agent）。
            你的任務是審查一份金融研究回答的品質，找出以下問題：
            1. UnsupportedClaim：回答中的結論沒有足夠的引用支撐
            2. WeakCitation：引用與結論之間的關聯性薄弱
            3. MissingCitation：重要結論沒有引用來源
            4. Contradiction：回答中的結論與引用內容矛盾
            5. InsufficientEvidence：引用的證據不足以支持結論的強度
            6. OverconfidentAnswer：回答使用了過於肯定的語氣但證據不足
            7. StaleEvidence：引用的資料可能已過時
            8. NumericalMismatch：回答中的數字與引用中的數字不一致

            請以 JSON 格式回覆，格式為：
            {
              "findings": [
                {
                  "severity": "Low|Medium|High|Critical",
                  "category": "UnsupportedClaim|WeakCitation|MissingCitation|Contradiction|InsufficientEvidence|OverconfidentAnswer|StaleEvidence|NumericalMismatch",
                  "message": "問題描述",
                  "relatedCitationIndexes": [0, 1],
                  "recommendation": "改進建議"
                }
              ]
            }

            如果回答品質良好，回傳空的 findings 陣列。
            """;

        var userPrompt = $"""
            ## 研究回答
            {answer}

            ## 引用清單
            {citationsText}

            ## 證據檢查結果
            {evidenceChecksText}

            請審查上述回答品質。
            """;

        try
        {
            var chatService = context.ServiceProvider.GetRequiredService<IChatCompletionService>();
            var result = await chatService.CompleteAsync(
                new ChatCompletionRequest(
                    SystemPrompt: systemPrompt,
                    UserPrompt: userPrompt,
                    Temperature: 0.2,
                    MaxTokens: 4096),
                ct);

            // Try to parse the critique as JSON
            var critiqueContent = result.Content;
            var findings = ParseCritiqueJson(critiqueContent);

            var output = new { criticFindings = findings };
            return new NodeExecutionResult(true, OutputJson: JsonSerializer.Serialize(output));
        }
        catch (Exception ex)
        {
            return new NodeExecutionResult(false, ErrorMessage: $"Critique failed: {ex.Message}");
        }
    }

    private static string FormatCitations(object? citationsRaw)
    {
        if (citationsRaw is JsonElement element && element.ValueKind == JsonValueKind.Array)
        {
            var items = new List<string>();
            int i = 0;
            foreach (var item in element.EnumerateArray())
            {
                var title = item.TryGetProperty("title", out var t) ? t.GetString() : "Unknown";
                var source = item.TryGetProperty("sourceType", out var s) ? s.GetString() : "Unknown";
                var quote = item.TryGetProperty("quoteText", out var q) ? q.GetString() : "";
                items.Add($"[{i}] {source}: {title} - {quote}");
                i++;
            }
            return items.Count > 0 ? string.Join("\n", items) : "(no citations)";
        }
        return "(no citations)";
    }

    private static object[] ParseCritiqueJson(string content)
    {
        try
        {
            // Try to extract JSON from the response (may be wrapped in markdown code blocks)
            var jsonStart = content.IndexOf('{');
            var jsonEnd = content.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonStr = content[jsonStart..(jsonEnd + 1)];
                var doc = JsonDocument.Parse(jsonStr);
                if (doc.RootElement.TryGetProperty("findings", out var findingsElement)
                    && findingsElement.ValueKind == JsonValueKind.Array)
                {
                    return JsonSerializer.Deserialize<object[]>(findingsElement.GetRawText())
                           ?? Array.Empty<object>();
                }
            }
        }
        catch
        {
            // Fall through to default
        }

        // Return a single finding noting the parse failure
        return [new
        {
            severity = "Medium",
            category = "InsufficientEvidence",
            message = "Could not parse critique response. Manual review recommended.",
            relatedCitationIndexes = Array.Empty<int>(),
            recommendation = "Review the answer manually."
        }];
    }
}
