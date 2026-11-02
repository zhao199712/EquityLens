using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Ai.Retrieval;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.Agents.Loop;

public sealed class ResearchAgentLoopNodeHandler(ResearchAgentLoop loop) : IAgentNodeHandler
{
    public string NodeType => ResearchAgentNodes.Loop;
    public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var revise = context.Node.NodeKey == "reviseAgent";
        var state = await loop.RunAsync(context, revise ? "Revise" : "Research", cancellationToken);
        if (!context.AwaitingApproval) await ResearchAgentOutput.WriteAsync(context, state, revise, cancellationToken);
    }
}

internal static partial class ResearchAgentOutput
{
    public static async Task WriteAsync(AgentNodeExecutionContext context, ResearchLoopState state, bool revise, CancellationToken ct)
    {
        var board = AgentNodeJson.ParseBlackboard(context.Run.BlackboardJson);
        var proposed = state.Answer!;
        var references = EvidenceReference().Matches(proposed.Markdown).Select(x => x.Groups[1].Value).Distinct().ToList();
        var selected = references.Select(id => state.Ledger.SingleOrDefault(x => x.Id == id)).Where(x => x is not null).Cast<LoopEvidence>().ToList();
        var indexes = selected.Select((x, i) => (x.Id, Index: i + 1)).ToDictionary(x => x.Id, x => x.Index);
        var answer = EvidenceReference().Replace(proposed.Markdown, match => indexes.TryGetValue(match.Groups[1].Value, out var index) ? $"[{index}]" : match.Value);
        var citations = selected.Select((x, i) => new
        {
            citationIndex = i + 1, sourceType = x.Chunk.SourceType.ToString(),
            documentChunkId = x.Chunk.SourceType == CitationSourceType.LocalDocument ? x.Chunk.Result.DocumentChunkId : (Guid?)null,
            documentId = x.Chunk.SourceType == CitationSourceType.LocalDocument ? x.Chunk.Result.DocumentId : (Guid?)null,
            title = x.Chunk.Result.DocumentTitle, x.Chunk.Result.DocumentType, x.Chunk.SourceRole, x.Chunk.Result.PageNumber,
            x.Chunk.Url, x.Chunk.PublishedAt, x.Chunk.RetrievedAt, quoteText = x.Chunk.Result.Content, x.Chunk.Result.RelevanceScore
        }).ToList();
        var candidates = state.Ledger.Select((x, i) => new ResearchRunCandidate
        {
            Id = Guid.NewGuid(), RunId = context.Run.ResearchRunId!.Value,
            SearchId = x.Chunk.SearchId, Query = x.Chunk.Query, DocumentChunkId = x.Chunk.Result.DocumentChunkId,
            DocumentId = x.Chunk.Result.DocumentId, Title = x.Chunk.Result.DocumentTitle, DocumentType = x.Chunk.Result.DocumentType,
            SourceRole = x.Chunk.SourceRole, PageNumber = x.Chunk.Result.PageNumber, RelevanceScore = x.Chunk.Result.RelevanceScore,
            RankBeforeRerank = i + 1, RankAfterRerank = i + 1, Decision = indexes.ContainsKey(x.Id) ? "Selected" : "Rejected",
            DiscardReason = indexes.ContainsKey(x.Id) ? null : "Not cited in answer", ContentPreview = AgentNodeJson.Trim(x.Chunk.Result.Content, 1000)
        }).ToList();
        board[AgentBlackboardKeys.Answer] = answer;
        if (revise) board[AgentBlackboardKeys.RevisedAnswer] = answer;
        board[AgentBlackboardKeys.Citations] = JsonSerializer.SerializeToNode(citations, AgentNodeJson.SerializerOptions);
        // Entity navigation 不進入 Blackboard。
        board[AgentBlackboardKeys.Candidates] = JsonSerializer.SerializeToNode(candidates.Select(x => new { x.Id, x.DocumentChunkId, x.DocumentId,
            x.Title, x.DocumentType, x.SourceRole, x.PageNumber, x.RelevanceScore, x.RankBeforeRerank, x.RankAfterRerank, x.Decision, x.DiscardReason, x.ContentPreview }), AgentNodeJson.SerializerOptions);
        board[AgentBlackboardKeys.SelectedEvidence] = JsonSerializer.SerializeToNode(new RankedSelection(selected.Select(x => x.Chunk).ToList(), []), AgentNodeJson.SerializerOptions);
        var status = answer == ResearchAgentLoop.Refusal ? "InsufficientEvidence" : "Answered";
        board[AgentBlackboardKeys.ResearchRun] = new JsonObject
        {
            ["run"] = new JsonObject { ["id"] = context.Run.ResearchRunId, ["ticker"] = board[AgentBlackboardKeys.Ticker]?.DeepClone(),
                ["question"] = board[AgentBlackboardKeys.Question]?.DeepClone(), ["status"] = status },
            ["answer"] = answer, ["steps"] = new JsonArray(),
            ["candidates"] = board[AgentBlackboardKeys.Candidates]!.DeepClone(), ["citations"] = board[AgentBlackboardKeys.Citations]!.DeepClone()
        };
        board["researchAgent"] = JsonSerializer.SerializeToNode(new { turns = state.Turn, state.StopReason, state.ToolCalls, state.WebCalls,
            state.GuardDenials, ledgerSize = state.Ledger.Count, mode = revise ? "Revise" : "Research", state.CostUsd }, AgentNodeJson.SerializerOptions);
        board["researchAgentProposedEvidenceIds"] = JsonSerializer.SerializeToNode(proposed.CitedEvidenceIds, AgentNodeJson.SerializerOptions);
        context.Run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);
        context.Node.OutputJson = AgentNodeJson.Serialize(new { answer, status, state.StopReason, citations = citations.Count });
        var db = context.DbContext;
        var artifact = await db.ResearchRuns.SingleAsync(x => x.Id == context.Run.ResearchRunId && x.UserId == context.Run.UserId, ct);
        artifact.Answer = answer; artifact.Status = "Running"; artifact.CitationCount = citations.Count;
        artifact.Model = (await db.AgentLlmTurns.Where(x => x.AgentRunNodeId == context.Node.Id).OrderByDescending(x => x.Turn).FirstOrDefaultAsync(ct))?.Model ?? "";
        db.ResearchRunCitations.RemoveRange(await db.ResearchRunCitations.Where(x => x.RunId == artifact.Id).ToListAsync(ct));
        db.ResearchRunCandidates.RemoveRange(await db.ResearchRunCandidates.Where(x => x.RunId == artifact.Id).ToListAsync(ct));
        db.ResearchRunCandidates.AddRange(candidates);
        db.ResearchRunCitations.AddRange(citations.Select(c => new ResearchRunCitation { Id = Guid.NewGuid(), RunId = artifact.Id,
            CitationIndex = c.citationIndex, SourceType = c.sourceType, DocumentChunkId = c.documentChunkId, DocumentId = c.documentId,
            Title = c.title, DocumentType = c.DocumentType, SourceRole = c.SourceRole, PageNumber = c.PageNumber,
            QuoteText = c.quoteText, RelevanceScore = c.RelevanceScore }));
    }
    [GeneratedRegex(@"\[(E\d+)\]")]
    private static partial Regex EvidenceReference();
}

public sealed class ResearchOutputGateNodeHandler : IAgentNodeHandler
{
    public string NodeType => ResearchAgentNodes.OutputGate;
    public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = AgentNodeJson.ParseBlackboard(context.Run.BlackboardJson);
        var result = Check(board);
        board["outputGate"] = JsonSerializer.SerializeToNode(result, AgentNodeJson.SerializerOptions);
        context.Run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);
        context.Node.OutputJson = AgentNodeJson.Serialize(result);
        context.AddEvent(context.Run, context.Node, "OutputGateEvaluated", result.Passed ? "Passed" : "Blocked", result);
        return Task.CompletedTask;
    }

    internal static OutputGateResult Check(JsonObject board)
    {
        var answer = board[AgentBlackboardKeys.Answer]?.GetValue<string>() ?? "";
        var request = board[AgentBlackboardKeys.ResearchRequest]!.Deserialize<ResearchAskRequest>(AgentNodeJson.SerializerOptions)!;
        var citations = (board[AgentBlackboardKeys.Citations]?.AsArray() ?? []).OfType<JsonObject>().ToList();
        var issues = new List<string>(); var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(answer) || answer.Length > 20000) issues.Add("InvalidAnswerSchema");
        if (Regex.IsMatch(answer, @"\[E\d+\]")) issues.Add("DanglingEvidenceReference");
        foreach (Match match in Regex.Matches(answer, @"\[(\d+)\]"))
            if (!int.TryParse(match.Groups[1].Value, out var index) || !citations.Any(x => x["citationIndex"]?.GetValue<int>() == index)) issues.Add("DanglingCitation");
        var ledgerIds = (board["researchAgentLedger"]?.AsArray() ?? []).Select(x => x?["id"]?.GetValue<string>()).ToHashSet();
        if ((board["researchAgentProposedEvidenceIds"]?.AsArray() ?? []).Any(x => !ledgerIds.Contains(x?.GetValue<string>()))) issues.Add("UnknownProposedEvidence");
        foreach (var c in citations)
        {
            var local = c["sourceType"]?.GetValue<string>() == nameof(CitationSourceType.LocalDocument);
            if ((request.SourcePolicy == SourcePolicy.LocalOnly && !local) || (request.SourcePolicy == SourcePolicy.WebOnly && local)) issues.Add("SourcePolicyViolation");
        }
        if (answer != ResearchAgentLoop.Refusal && citations.Count == 0) issues.Add("MissingCitations");
        var numberCheck = ResearchNumberTraceability.Check(answer, citations);
        warnings.AddRange(numberCheck.Warnings);
        if (numberCheck.Blocking) issues.Add("UntraceableNumbers");
        if (Regex.IsMatch(answer, @"(?<!不)(?<!無法)(?<!不能)保證獲利")) issues.Add("GuaranteedProfitClaim");
        return new(issues.Count == 0, issues.Distinct().ToArray(), warnings.Distinct().ToArray());
    }
}
public sealed record OutputGateResult(bool Passed, IReadOnlyList<string> BlockingIssues, IReadOnlyList<string> Warnings);

public sealed class RouteResearchAgentQualityNodeHandler : IAgentNodeHandler
{
    public string NodeType => ResearchAgentNodes.Route;
    public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = AgentNodeJson.ParseBlackboard(context.Run.BlackboardJson);
        var accepted = board["outputGate"]?["passed"]?.GetValue<bool>() == true
            && board[AgentBlackboardKeys.CriticReview]?[CriticReviewFields.RecommendedNextAction]?.GetValue<string>() == "AcceptAnswer";
        board["qualityRoute"] = new JsonObject { ["decision"] = accepted ? "Accept" : "Revise" };
        context.Node.OutputJson = board["qualityRoute"]!.ToJsonString();
        context.Run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);
        return Task.CompletedTask;
    }
}

public sealed class FinalizeResearchAgentNodeHandler : IAgentNodeHandler
{
    public string NodeType => ResearchAgentNodes.Finalize;
    public async Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var board = AgentNodeJson.ParseBlackboard(context.Run.BlackboardJson);
        var passed = board["outputGate"]?["passed"]?.GetValue<bool>() == true;
        var accepted = board[AgentBlackboardKeys.CriticReview]?[CriticReviewFields.RecommendedNextAction]?.GetValue<string>() == "AcceptAnswer";
        var refusal = !passed || !accepted || board[AgentBlackboardKeys.Answer]?.GetValue<string>() == ResearchAgentLoop.Refusal;
        // 這是系統固定的風險說明，不把未通過驗收的模型答案升級為成功。
        var policyAnswer = refusal ? GuaranteedReturnResponse(board[AgentBlackboardKeys.Question]?.GetValue<string>() ?? "") : null;
        var answer = policyAnswer ?? (refusal ? ResearchAgentLoop.Refusal : board[AgentBlackboardKeys.Answer]!.GetValue<string>());
        board["researchAgentPolicyResponse"] = policyAnswer is not null;
        board[AgentBlackboardKeys.Answer] = answer;
        if (board[AgentBlackboardKeys.RevisedAnswer] is not null) board[AgentBlackboardKeys.RevisedAnswer] = answer;
        board["researchAgentFinalStatus"] = policyAnswer is not null ? "Answered" : refusal ? (passed ? "InsufficientEvidence" : "CitationValidationFailed") : "Answered";
        board[AgentBlackboardKeys.FinalOutput] = new JsonObject { ["answer"] = answer, ["status"] = board["researchAgentFinalStatus"]!.DeepClone(),
            ["policyResponse"] = policyAnswer is not null,
            ["criticReview"] = board[AgentBlackboardKeys.CriticReview]?.DeepClone(), ["outputGate"] = board["outputGate"]?.DeepClone() };
        if (refusal)
        {
            board[AgentBlackboardKeys.Citations] = new JsonArray();
            context.DbContext.ResearchRunCitations.RemoveRange(await context.DbContext.ResearchRunCitations
                .Where(x => x.RunId == context.Run.ResearchRunId).ToListAsync(cancellationToken));
            var artifact = await context.DbContext.ResearchRuns.SingleAsync(x => x.Id == context.Run.ResearchRunId, cancellationToken);
            artifact.CitationCount = 0;
        }
        if (board[AgentBlackboardKeys.ResearchRun] is JsonObject research)
        {
            research["answer"] = answer;
            research["citations"] = board[AgentBlackboardKeys.Citations]?.DeepClone();
            if (research["run"] is JsonObject summary) summary["status"] = board["researchAgentFinalStatus"]!.DeepClone();
        }
        context.Run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);
        context.Run.OutputJson = board[AgentBlackboardKeys.FinalOutput]!.ToJsonString();
        context.Node.OutputJson = context.Run.OutputJson;
    }
    internal static string? GuaranteedReturnResponse(string question)
    {
        // 僅辨識要求投資報酬承諾的問句；保證金／保固等一般文件問題不適用。
        if (!Regex.IsMatch(question, @"(?:一定|必定|肯定).{0,8}(?:賺錢|賺|獲利)|(?:保證|穩賺).{0,8}(?:獲利|報酬|賺錢|不賠)|guaranteed?\s+(?:profit|return)", RegexOptions.IgnoreCase)) return null;
        return "投資有風險，未來報酬存在不確定性，無法保證買進後能獲利。請依自身風險承受能力評估，歷史績效不代表未來結果。";
    }

}
