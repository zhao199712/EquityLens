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
            throw new InvalidOperationException("Research run not found.");
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
        var researchRun = AgentNodeJson.GetRequiredBlackboardObject(blackboard, AgentBlackboardKeys.ResearchRun);
        var runSummary = researchRun["run"]?.AsObject() ?? throw new InvalidOperationException("Research run summary is missing.");
        var citations = researchRun["citations"]?.AsArray() ?? [];
        var candidates = researchRun["candidates"]?.AsArray() ?? [];
        var status = runSummary["status"]?.GetValue<string>() ?? string.Empty;
        node.InputJson = AgentNodeJson.Serialize(new CheckEvidenceNodeInput(
            AgentNodeJson.GetBlackboardValue<string>(blackboard, AgentBlackboardKeys.Ticker),
            status,
            citations.Count,
            candidates.Count));

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
        if (candidates.Count < 3)
        {
            findings.Add(AgentBlackboardContracts.CreateFinding("Medium", "InsufficientEvidence", "候選證據數量偏少。", "增加檢索 topK 或改用 web/local 混合來源。"));
        }

        var evidenceChecks = AgentBlackboardContracts.CreateEvidenceChecks(citations.Count, candidates.Count, status, findings);
        blackboard[AgentBlackboardKeys.EvidenceChecks] = evidenceChecks;
        blackboard[AgentBlackboardKeys.CriticFindings] = findings;
        run.BlackboardJson = blackboard.ToJsonString(AgentNodeJson.SerializerOptions);
        node.OutputJson = AgentNodeJson.Serialize(new CheckEvidenceNodeOutput(
            citations.Count,
            candidates.Count,
            status,
            findings.Count,
            findings.Select(AgentNodeJson.ParseFinding).Where(x => x is not null).Cast<CriticFinding>().ToList()));
        context.AddEvent(run, node, AgentEventTypes.BlackboardUpdated, "Evidence checks written to blackboard.", new { findingCount = findings.Count });
        return Task.CompletedTask;
    }
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

        var result = await _criticReviewAgent.CritiqueAsync(input, cancellationToken);
        blackboard[AgentBlackboardKeys.CriticReview] = JsonSerializer.SerializeToNode(result, AgentNodeJson.SerializerOptions);
        blackboard[AgentBlackboardKeys.CriticFindings] = JsonSerializer.SerializeToNode(result.Findings, AgentNodeJson.SerializerOptions);
        run.BlackboardJson = blackboard.ToJsonString(AgentNodeJson.SerializerOptions);
        node.OutputJson = AgentNodeJson.Serialize(result);
        context.AddEvent(run, node, AgentEventTypes.BlackboardUpdated, "Critic review result written to blackboard.", new { findingCount = result.Findings.Count, result.OverallSeverity });
    }
}

public sealed class FinalizeCriticReportNodeHandler : IAgentNodeHandler
{
    public string NodeType => CriticReviewNodeTypes.FinalizeCriticReport;

    public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var run = context.Run;
        var node = context.Node;
        var blackboard = AgentNodeJson.ParseBlackboard(run.BlackboardJson);
        var criticReview = AgentNodeJson.GetRequiredBlackboardObject(blackboard, AgentBlackboardKeys.CriticReview);
        var finalOutput = AgentNodeJson.CreateFinalizeCriticReportNodeOutput(criticReview);
        node.InputJson = AgentNodeJson.Serialize(new FinalizeCriticReportNodeInput(
            finalOutput.OverallSeverity,
            finalOutput.RequiresRevision,
            finalOutput.RequiresMoreEvidence,
            finalOutput.RouteBackTo,
            finalOutput.RecommendedNextAction));
        node.OutputJson = AgentNodeJson.Serialize(finalOutput);
        blackboard[AgentBlackboardKeys.FinalOutput] = JsonSerializer.SerializeToNode(finalOutput, AgentNodeJson.SerializerOptions);
        run.BlackboardJson = blackboard.ToJsonString(AgentNodeJson.SerializerOptions);
        run.OutputJson = node.OutputJson;
        context.AddEvent(run, node, AgentEventTypes.BlackboardUpdated, "Final critic report written to blackboard.", new { overallSeverity = finalOutput.OverallSeverity });
        return Task.CompletedTask;
    }
}
