using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.Agents.Loop;

/// <summary>副作用授權綁定工具、參數及 execution attempt；工具本身必須實作冪等鍵。</summary>
internal static class ResearchToolApproval
{
    internal static string Hash(string json) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(JsonNode.Parse(json))!.ToJsonString())));
    private static JsonNode? Canonical(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new KeyValuePair<string, JsonNode?>(x.Key, Canonical(x.Value)))),
        JsonArray array => new JsonArray(array.Select(Canonical).ToArray()),
        _ => node?.DeepClone()
    };

    internal static async Task<bool> PrepareAsync(AgentNodeExecutionContext context, IReadOnlyList<AgentToolCall> records,
        LoopToolScope scope, IResearchLoopTools tools, ResearchAgentLoopOptions options, ResearchLoopState state, DateTime now, CancellationToken ct)
    {
        foreach (var record in records.Where(x => x.GuardDecision == "RequireApproval" && x.Status is "AwaitingApproval" or "Running"))
        {
            var db = context.DbContext;
            var approval = await db.AgentApprovalRequests.SingleOrDefaultAsync(x => x.AgentToolCallId == record.Id, ct);
            if (approval is null)
            {
                approval = new AgentApprovalRequest
                {
                    Id = Guid.NewGuid(), AgentRunId = context.Run.Id, AgentRunNodeId = context.Node.Id,
                    NodeKey = context.Node.NodeKey, NodeType = context.Node.NodeType, Scope = "ToolCall",
                    AgentToolCallId = record.Id, ToolName = record.ToolName, ArgumentsJson = record.ArgumentsJson,
                    ArgumentsHash = Hash(record.ArgumentsJson), IdempotencyKey = $"{context.Run.Id:N}:{record.Id:N}",
                    LeaseVersionAtRequest = context.Run.LeaseVersion, ExecutionAttempt = context.Run.ExecutionAttempt,
                    Status = AgentApprovalStatuses.Pending, SideEffectLevel = "ExternalWrite",
                    Reason = "此工具可能產生外部副作用，請確認工具及完整參數。", RequestedAtUtc = now
                };
                db.AgentApprovalRequests.Add(approval);
                context.AddEvent(context.Run, context.Node, "ToolApprovalRequested", approval.Reason,
                    new { approval.Id, record.ToolName, approval.ArgumentsHash });
            }
            if (approval.Status == AgentApprovalStatuses.Pending)
            {
                context.RequestApproval();
                return false;
            }
            var call = new LoopToolCall(record.ToolCallKey!, record.ToolName, JsonNode.Parse(record.ArgumentsJson)!.AsObject());
            var reason = approval.Status != AgentApprovalStatuses.Approved ? "HumanRejected: " + approval.DecisionComment
                : approval.ConsumedAtUtc is not null ? "ApprovalAlreadyConsumed"
                : approval.ToolName != record.ToolName || approval.ArgumentsHash != Hash(record.ArgumentsJson) ? "ApprovalArgumentsChanged"
                : approval.ExecutionAttempt != context.Run.ExecutionAttempt ? "ApprovalAttemptChanged"
                : now - approval.RequestedAtUtc > TimeSpan.FromHours(options.ApprovalExpiryHours) ? "ApprovalExpired"
                : state.CostUsd > options.MaxCostUsd || state.ToolCalls > options.MaxToolCalls || state.WebCalls > options.MaxWebCalls ? "ApprovalBudgetChanged"
                : tools.Validate(scope, call);
            // 成本已在最初 dispatch 前預留；恢復不能再次計費或超出現有保留。
            context.AddEvent(context.Run, context.Node, "ToolApprovalRevalidated", reason ?? "Allowed", new { approval.Id });
            if (reason is not null)
            {
                state.GuardDenials++;
                record.Status = "Denied"; record.GuardReason = reason; record.CompletedAtUtc = now;
            }
            else record.Status = "Running";
        }
        return true;
    }
}
