using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Research;

namespace EquityLens.Api.Services.Agents;

public sealed class LoadResearchRunNodeHandler : IAgentNodeHandler
{
    private readonly IResearchRunTraceService _researchTraceService;

    public LoadResearchRunNodeHandler(IResearchRunTraceService researchTraceService)
    {
        _researchTraceService = researchTraceService;
    }

    public string NodeType => CriticReviewNodeTypes.LoadResearchRun;

    public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var run = context.Run;
        var node = context.Node;
        var researchRunId = GetResearchRunId(run.InputJson);
        var input = new LoadResearchRunNodeInput(researchRunId);
        node.InputJson = AgentNodeJson.Serialize(input);

        var toolCall = new AgentToolCall
        {
            Id = Guid.NewGuid(),
            AgentRunId = run.Id,
            AgentRunNodeId = node.Id,
            ToolName = "getResearchRun",
            Status = AgentToolCallStatuses.Running,
            ArgumentsJson = AgentNodeJson.Serialize(input),
            StartedAtUtc = DateTime.UtcNow
        };
        context.DbContext.AgentToolCalls.Add(toolCall);
        context.AddEvent(run, node, AgentEventTypes.ToolCallStarted, "Tool getResearchRun started.", new { researchRunId });
        await context.DbContext.SaveChangesAsync(cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        var detail = await _researchTraceService.GetByIdAsync(researchRunId, run.UserId, cancellationToken);
        stopwatch.Stop();
        if (detail is null)
        {
            toolCall.Status = AgentToolCallStatuses.Failed;
            toolCall.ErrorMessage = "Research run not found.";
            toolCall.CompletedAtUtc = DateTime.UtcNow;
            toolCall.DurationMs = stopwatch.ElapsedMilliseconds;
            context.AddEvent(run, node, AgentEventTypes.ToolCallFailed, "Tool getResearchRun failed.", new { error = toolCall.ErrorMessage });
            throw new AgentNodeException("research_run_not_found", AgentNodeErrorCategories.PermanentFailure, "Research run not found.", retryable: false);
        }

        var resultJson = AgentNodeJson.Serialize(detail);
        toolCall.Status = AgentToolCallStatuses.Succeeded;
        toolCall.ResultJson = resultJson;
        toolCall.ResultPreview = $"{detail.Run.Ticker} {detail.Run.Status}: {AgentNodeJson.Trim(detail.Run.Question, 160)}";
        toolCall.CompletedAtUtc = DateTime.UtcNow;
        toolCall.DurationMs = stopwatch.ElapsedMilliseconds;
        context.AddEvent(run, node, AgentEventTypes.ToolCallCompleted, "Tool getResearchRun completed.", new { toolCall.DurationMs });

        var blackboard = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        blackboard[AgentBlackboardKeys.Ticker] = detail.Run.Ticker;
        blackboard[AgentBlackboardKeys.Question] = detail.Run.Question;
        blackboard[AgentBlackboardKeys.ResearchRun] = JsonSerializer.SerializeToNode(detail, AgentNodeJson.SerializerOptions);
        blackboard[AgentBlackboardKeys.Answer] = detail.Answer;
        blackboard[AgentBlackboardKeys.Citations] = JsonSerializer.SerializeToNode(detail.Citations, AgentNodeJson.SerializerOptions);
        blackboard[AgentBlackboardKeys.Steps] = JsonSerializer.SerializeToNode(detail.Steps, AgentNodeJson.SerializerOptions);
        blackboard[AgentBlackboardKeys.Candidates] = JsonSerializer.SerializeToNode(detail.Candidates, AgentNodeJson.SerializerOptions);
        run.BlackboardJson = blackboard.ToJsonString(AgentNodeJson.SerializerOptions);
        node.OutputJson = AgentNodeJson.Serialize(new LoadResearchRunNodeOutput(
            detail.Run.Ticker,
            detail.Run.Status,
            detail.Citations.Count,
            detail.Candidates.Count,
            !string.IsNullOrWhiteSpace(detail.Answer)));
        context.AddEvent(run, node, AgentEventTypes.BlackboardUpdated, "Research run detail loaded into blackboard.", new { keys = new[] { AgentBlackboardKeys.Ticker, AgentBlackboardKeys.Question, AgentBlackboardKeys.ResearchRun, AgentBlackboardKeys.Citations, AgentBlackboardKeys.Steps, AgentBlackboardKeys.Candidates } });
    }

    private static Guid GetResearchRunId(string inputJson)
    {
        using var document = JsonDocument.Parse(inputJson);
        return document.RootElement.GetProperty("researchRunId").GetGuid();
    }
}

public sealed class CheckEvidenceNodeHandler : IAgentNodeHandler
{
    public string NodeType => CriticReviewNodeTypes.CheckEvidence;

    public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var run = context.Run;
        var node = context.Node;
        var blackboard = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        var evidencePacket = AgentNodeJson.GetRequiredBlackboardObject(blackboard, AgentBlackboardKeys.EvidencePacket);
        var citations = evidencePacket["citations"]?.AsArray() ?? [];
        var status = evidencePacket["sourceStatus"]?.GetValue<string>() ?? string.Empty;
        var candidateCount = evidencePacket["candidateCount"]?.GetValue<int>() ?? 0;
        node.InputJson = AgentNodeJson.Serialize(new CheckEvidenceNodeInput(
            evidencePacket[AgentBlackboardKeys.Ticker]?.GetValue<string>(),
            status,
            citations.Count,
            candidateCount));

        var findings = new JsonArray();
        if (citations.Count == 0)
        {
            findings.Add(AgentBlackboardContracts.CreateFinding("Critical", "MissingCitation", "回答沒有任何引用來源。", "補充可驗證的文件引用後再生成回答。"));
        }
        if (string.Equals(status, "InsufficientEvidence", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(AgentBlackboardContracts.CreateFinding("High", "InsufficientEvidence", "原始回答已標示資料不足。", "應先補充檢索來源或改寫為資料不足回覆。"));
        }
        if (string.Equals(status, "CitationValidationFailed", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(AgentBlackboardContracts.CreateFinding("High", "WeakCitation", "原始回答的 citation validation 失敗。", "檢查引用編號與引用內容是否能支撐回答句子。"));
        }

        var emptyQuoteCount = citations.Count(c =>
            string.IsNullOrWhiteSpace(c?["quoteText"]?.GetValue<string>()));
        if (emptyQuoteCount > 0)
        {
            findings.Add(AgentBlackboardContracts.CreateFinding("Medium", "WeakCitation", $"有 {emptyQuoteCount} 個 citation 缺少 quoteText。", "補齊句級引用內容，避免只引用文件標題。"));
        }
        if (candidateCount < 3)
        {
            findings.Add(AgentBlackboardContracts.CreateFinding("Medium", "InsufficientEvidence", "候選證據數量偏少。", "增加檢索 topK 或改用 web/local 混合來源。"));
        }

        var evidenceChecks = AgentBlackboardContracts.CreateEvidenceChecks(citations.Count, candidateCount, status, findings);
        blackboard[AgentBlackboardKeys.EvidenceChecks] = evidenceChecks;
        blackboard[AgentBlackboardKeys.CriticFindings] = findings;
        run.BlackboardJson = blackboard.ToJsonString(AgentNodeJson.SerializerOptions);
        node.OutputJson = AgentNodeJson.Serialize(new CheckEvidenceNodeOutput(
            citations.Count,
            candidateCount,
            status,
            findings.Count,
            findings.Select(AgentNodeJson.ParseFinding).Where(x => x is not null).Cast<CriticFinding>().ToList()));
        context.AddEvent(run, node, AgentEventTypes.BlackboardUpdated, "Evidence checks written to blackboard.", new { findingCount = findings.Count });
        return Task.CompletedTask;
    }
}

public sealed class BuildEvidencePacketNodeHandler : IAgentNodeHandler
{
    public string NodeType => CriticReviewNodeTypes.BuildEvidencePacket;

    public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var run = context.Run;
        var node = context.Node;
        var blackboard = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        var researchRun = AgentNodeJson.GetRequiredBlackboardObject(blackboard, AgentBlackboardKeys.ResearchRun);
        var runSummary = researchRun["run"]?.AsObject() ?? throw new AgentNodeException("research_run_summary_missing", AgentNodeErrorCategories.ValidationFailure, "Research run summary is missing.");
        var citations = researchRun["citations"]?.AsArray() ?? [];
        var candidates = researchRun["candidates"]?.AsArray() ?? [];
        var ticker = runSummary["ticker"]?.GetValue<string>();
        var question = runSummary["question"]?.GetValue<string>();
        var sourceStatus = runSummary["status"]?.GetValue<string>() ?? string.Empty;
        var answer = AgentNodeJson.GetBlackboardValue<string>(blackboard, AgentBlackboardKeys.Answer);
        var packetCitations = citations
            .Select(CreateEvidencePacketCitation)
            .ToArray();
        var emptyQuoteCount = packetCitations.Count(c => string.IsNullOrWhiteSpace(c.QuoteText));
        var selectedCandidateCount = candidates.Count(c =>
            string.Equals(c?["decision"]?.GetValue<string>(), "Selected", StringComparison.OrdinalIgnoreCase));
        var summary = new EvidencePacketSummary(
            !string.IsNullOrWhiteSpace(answer),
            packetCitations.Length > 0,
            emptyQuoteCount,
            selectedCandidateCount);
        var packet = new EvidencePacket(
            ticker,
            question,
            answer,
            sourceStatus,
            packetCitations.Length,
            candidates.Count,
            packetCitations,
            summary);

        node.InputJson = AgentNodeJson.Serialize(new BuildEvidencePacketNodeInput(
            ticker,
            sourceStatus,
            citations.Count,
            candidates.Count,
            summary.HasAnswer));
        node.OutputJson = AgentNodeJson.Serialize(new BuildEvidencePacketNodeOutput(
            ticker,
            sourceStatus,
            packet.CitationCount,
            packet.CandidateCount,
            summary));
        blackboard[AgentBlackboardKeys.EvidencePacket] = JsonSerializer.SerializeToNode(packet, AgentNodeJson.SerializerOptions);
        run.BlackboardJson = blackboard.ToJsonString(AgentNodeJson.SerializerOptions);
        context.AddEvent(run, node, AgentEventTypes.BlackboardUpdated, "Evidence packet written to blackboard.", new { packet.CitationCount, packet.CandidateCount, summary.EmptyQuoteCount });
        return Task.CompletedTask;
    }

    private static EvidencePacketCitation CreateEvidencePacketCitation(JsonNode? citation)
    {
        var value = citation?.AsObject();
        return new EvidencePacketCitation(
            GetInt(value, "citationIndex") ?? 0,
            GetString(value, "sourceType"),
            GetGuid(value, "documentId"),
            GetGuid(value, "documentChunkId"),
            GetString(value, "title"),
            GetString(value, "documentType"),
            GetInt(value, "pageNumber"),
            GetString(value, "quoteText"));
    }

    private static string? GetString(JsonObject? value, string key) => value?[key] is null ? null : value[key]!.GetValue<string>();

    private static Guid? GetGuid(JsonObject? value, string key) => value?[key] is null ? null : value[key]!.GetValue<Guid>();

    private static int? GetInt(JsonObject? value, string key) => value?[key] is null ? null : value[key]!.GetValue<int>();
}

public sealed class CritiqueAnswerNodeHandler : IAgentNodeHandler
{
    private readonly ICriticReviewAgent _criticReviewAgent;

    public CritiqueAnswerNodeHandler(ICriticReviewAgent criticReviewAgent)
    {
        _criticReviewAgent = criticReviewAgent;
    }

    public string NodeType => CriticReviewNodeTypes.CritiqueAnswer;

    public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var run = context.Run;
        var node = context.Node;
        var blackboard = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        var input = AgentNodeJson.CreateCriticReviewInput(blackboard);
        node.InputJson = AgentNodeJson.Serialize(input);

        var toolCall = new AgentToolCall
        {
            Id = Guid.NewGuid(),
            AgentRunId = run.Id,
            AgentRunNodeId = node.Id,
            ToolName = "criticReviewLLM",
            Status = AgentToolCallStatuses.Running,
            ArgumentsJson = AgentNodeJson.Serialize(new
            {
                input.Ticker,
                input.CitationCount,
                input.CandidateCount,
                input.SourceStatus,
                EvidenceFindingCount = input.EvidenceFindings.Count,
                AnswerPreview = AgentNodeJson.Trim(input.Answer ?? string.Empty, 240)
            }),
            StartedAtUtc = DateTime.UtcNow
        };
        context.DbContext.AgentToolCalls.Add(toolCall);
        context.AddEvent(run, node, AgentEventTypes.ToolCallStarted, "Tool criticReviewLLM started.", new { input.Ticker, input.CitationCount, input.CandidateCount });
        await context.DbContext.SaveChangesAsync(cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        CriticReviewResult result;
        try
        {
            result = await _criticReviewAgent.CritiqueAsync(input, cancellationToken);
            result = MergeEvidenceFindings(input, result);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            toolCall.Status = AgentToolCallStatuses.Failed;
            toolCall.ErrorMessage = exception.Message;
            toolCall.CompletedAtUtc = DateTime.UtcNow;
            toolCall.DurationMs = stopwatch.ElapsedMilliseconds;
            context.AddEvent(run, node, AgentEventTypes.ToolCallFailed, "Tool criticReviewLLM failed.", new { error = exception.Message });
            await context.DbContext.SaveChangesAsync(cancellationToken);
            throw;
        }

        stopwatch.Stop();
        toolCall.Status = AgentToolCallStatuses.Succeeded;
        toolCall.ResultJson = AgentNodeJson.Serialize(result);
        toolCall.ResultPreview = $"{result.OverallSeverity}: {AgentNodeJson.Trim(result.Summary, 180)}";
        toolCall.CompletedAtUtc = DateTime.UtcNow;
        toolCall.DurationMs = stopwatch.ElapsedMilliseconds;
        context.AddEvent(run, node, AgentEventTypes.ToolCallCompleted, "Tool criticReviewLLM completed.", new { toolCall.DurationMs, result.OverallSeverity, FindingCount = result.Findings.Count });
        blackboard[AgentBlackboardKeys.CriticReview] = JsonSerializer.SerializeToNode(result, AgentNodeJson.SerializerOptions);
        blackboard[AgentBlackboardKeys.CriticFindings] = JsonSerializer.SerializeToNode(result.Findings, AgentNodeJson.SerializerOptions);
        run.BlackboardJson = blackboard.ToJsonString(AgentNodeJson.SerializerOptions);
        node.OutputJson = AgentNodeJson.Serialize(result);
        context.AddEvent(run, node, AgentEventTypes.BlackboardUpdated, "Critic review result written to blackboard.", new { findingCount = result.Findings.Count, result.OverallSeverity });
    }

    private static CriticReviewResult MergeEvidenceFindings(CriticReviewInput input, CriticReviewResult result)
    {
        if (input.EvidenceFindings.Count == 0)
        {
            return result;
        }

        var findings = input.EvidenceFindings.ToList();
        foreach (var finding in result.Findings)
        {
            if (!findings.Any(existing => IsSameFindingKind(existing, finding)))
            {
                findings.Add(finding);
            }
        }
        var summary = findings.Count == input.EvidenceFindings.Count
            ? $"系統證據檢查發現 {input.EvidenceFindings.Count} 個證據覆蓋問題，需先補強證據或修訂回答。"
            : $"系統證據檢查發現 {input.EvidenceFindings.Count} 個證據覆蓋問題。{result.Summary}";

        return result with
        {
            Summary = summary,
            OverallSeverity = DetermineOverallSeverity(findings),
            Findings = findings.ToArray()
        };
    }

    private static bool IsSameFindingKind(CriticFinding left, CriticFinding right) =>
        string.Equals(left.Category, right.Category, StringComparison.OrdinalIgnoreCase)
        && left.RelatedCitationIndexes.SequenceEqual(right.RelatedCitationIndexes);

    private static string DetermineOverallSeverity(IEnumerable<CriticFinding> findings)
    {
        var severities = findings.Select(f => f.Severity).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (severities.Contains("Critical")) return "Critical";
        if (severities.Contains("High")) return "High";
        if (severities.Contains("Medium")) return "Medium";
        if (severities.Contains("Low")) return "Low";
        return "None";
    }

}

public sealed class FinalizeCriticReportNodeHandler : IAgentNodeHandler
{
    private readonly IReadOnlyDictionary<string, IWorkflowPolicyEvaluator> _policyEvaluators;

    public FinalizeCriticReportNodeHandler(IEnumerable<IWorkflowPolicyEvaluator> policyEvaluators)
    {
        _policyEvaluators = policyEvaluators.ToDictionary(x => x.WorkflowType, StringComparer.Ordinal);
    }

    public string NodeType => CriticReviewNodeTypes.FinalizeCriticReport;

    public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var run = context.Run;
        var node = context.Node;
        var blackboard = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        var criticReview = AgentNodeJson.GetRequiredBlackboardObject(blackboard, AgentBlackboardKeys.CriticReview);
        if (!_policyEvaluators.TryGetValue(run.WorkflowType, out var policyEvaluator))
        {
            throw new AgentNodeException("unsupported_policy_evaluator", AgentNodeErrorCategories.PermanentFailure, $"Unsupported workflow policy evaluator for '{run.WorkflowType}'.");
        }

        var policyDecision = policyEvaluator.Evaluate(new WorkflowPolicyContext(
            run.WorkflowType,
            node.NodeKey,
            blackboard,
            criticReview));
        var finalOutput = AgentNodeJson.CreateFinalizeCriticReportNodeOutput(criticReview, policyDecision);
        node.InputJson = AgentNodeJson.Serialize(new FinalizeCriticReportNodeInput(
            finalOutput.OverallSeverity,
            finalOutput.RequiresRevision,
            finalOutput.RequiresMoreEvidence,
            finalOutput.RouteBackTo,
            finalOutput.RecommendedNextAction));
        node.OutputJson = AgentNodeJson.Serialize(finalOutput);
        blackboard[AgentBlackboardKeys.FinalOutput] = JsonSerializer.SerializeToNode(finalOutput, AgentNodeJson.SerializerOptions);
        criticReview[CriticReviewFields.RequiresRevision] = finalOutput.RequiresRevision;
        criticReview[CriticReviewFields.RequiresMoreEvidence] = finalOutput.RequiresMoreEvidence;
        criticReview[CriticReviewFields.RouteBackTo] = finalOutput.RouteBackTo is null ? null : JsonSerializer.SerializeToNode(finalOutput.RouteBackTo, AgentNodeJson.SerializerOptions);
        criticReview[CriticReviewFields.RecommendedNextAction] = finalOutput.RecommendedNextAction;
        run.BlackboardJson = blackboard.ToJsonString(AgentNodeJson.SerializerOptions);
        run.OutputJson = node.OutputJson;
        context.AddEvent(run, node, AgentEventTypes.BlackboardUpdated, "Final critic report written to blackboard.", new { overallSeverity = finalOutput.OverallSeverity, policyDecision.RecommendedNextAction, policyDecision.Reason });
        return Task.CompletedTask;
    }
}
