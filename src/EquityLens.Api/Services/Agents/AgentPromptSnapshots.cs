using System.Security.Cryptography;
using System.Text;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.Agents;

public sealed record PromptUsageDefinition(string UsageKey, string OwnerType, string OwnerKey, string Name, string Description);

/// <summary>V3 workflow 的穩定 prompt usage keys。此 catalog 僅做 bootstrap 與 snapshot 選擇，不是 runtime fallback。</summary>
public static class AgentPromptUsageCatalog
{
    public static readonly IReadOnlyList<PromptUsageDefinition> All =
    [
        new("agent.router.investment-research", "Agent", "InvestmentResearchRouter", "角色路由", "研究／投組角色與參數。"),
        new("agent.critic.review", "Agent", "LlmCriticReviewAgent", "評論答案", "檢查答案和證據品質。")
    ];
    // Agent loop rendered prompts and loaded method hashes are persisted in turns/tool calls.
    public static IReadOnlyList<PromptUsageDefinition> ForWorkflow(string workflowType) => workflowType is AgentWorkflowTypes.CriticReview or AgentWorkflowTypes.ResearchAgent
        ? [Find("agent.critic.review")] : [];
    public static PromptUsageDefinition Find(string key) => All.Single(x => x.UsageKey == key);
}

public interface IAgentPromptSnapshotService
{
    Task SnapshotForRunAsync(AgentRun run, CancellationToken ct = default);
}

public sealed class AgentPromptSnapshotService(EquityLensDbContext db) : IAgentPromptSnapshotService
{
    public async Task SnapshotForRunAsync(AgentRun run, CancellationToken ct = default)
    {
        var usages = AgentPromptUsageCatalog.ForWorkflow(run.WorkflowType);
        if (usages.Count == 0) return;
        var keys = usages.Select(x => x.UsageKey).ToArray();
        var bindings = await db.PromptBindings.Include(x => x.Template).Include(x => x.Version).Where(x => keys.Contains(x.UsageKey) && x.IsActive).ToListAsync(ct);
        foreach (var usage in usages)
        {
            var binding = bindings.SingleOrDefault(x => x.UsageKey == usage.UsageKey)
                ?? throw new PromptManagementException("prompt_configuration_missing", $"缺少 prompt binding: {usage.UsageKey}");
            if (binding.Template.Status != "Active" || binding.Version.Status is not ("Published" or "Retired"))
                throw new PromptManagementException("prompt_configuration_missing", $"Prompt binding 無可執行版本: {usage.UsageKey}");
            db.AgentRunPromptSnapshots.Add(new AgentRunPromptSnapshot { Id = Guid.NewGuid(), AgentRunId = run.Id, UsageKey = usage.UsageKey, OwnerType = usage.OwnerType, OwnerKey = usage.OwnerKey, PromptTemplateId = binding.Template.Id, PromptTemplateKey = binding.Template.Key, PromptVersionId = binding.Version.Id, PromptVersionNumber = binding.Version.VersionNumber, SystemPrompt = binding.Version.SystemPrompt, UserPrompt = binding.Version.UserPrompt, RequiredVariablesJson = binding.Version.RequiredVariablesJson, ContentHash = binding.Version.ContentHash, ResolvedAtUtc = DateTime.UtcNow });
        }
    }
}

/// <summary>在已升級資料庫第一次啟動時建立現有程式 prompt 的可追蹤基線；執行期不讀此 catalog。</summary>
public sealed class PromptManagementBootstrapService(EquityLensDbContext db)
{
    public async Task EnsureSeededAsync(CancellationToken ct = default)
    {
        if (await db.PromptTemplates.AnyAsync(ct)) return;
        var actor = Guid.Empty; var now = DateTime.UtcNow;
        foreach (var usage in AgentPromptUsageCatalog.All)
        {
            var key = usage.UsageKey.Replace('.', '-');
            var text = $"EquityLens managed prompt baseline for {usage.UsageKey}. Preserve the existing V3 safety, evidence, and language requirements for this capability.";
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text + "\u001f[]"))).ToLowerInvariant();
            var template = new PromptTemplate { Id = Guid.NewGuid(), Key = key, Name = usage.Name, Description = usage.Description, CreatedAtUtc = now, UpdatedAtUtc = now, CreatedByUserId = actor, UpdatedByUserId = actor };
            var version = new PromptVersion { Id = Guid.NewGuid(), PromptTemplateId = template.Id, VersionNumber = 1, Status = "Published", SystemPrompt = text, RequiredVariablesJson = "[]", ContentHash = hash, CreatedAtUtc = now, CreatedByUserId = actor, PublishedAtUtc = now, PublishedByUserId = actor };
            var binding = new PromptBinding { Id = Guid.NewGuid(), UsageKey = usage.UsageKey, OwnerType = usage.OwnerType, OwnerKey = usage.OwnerKey, PromptTemplateId = template.Id, PromptVersionId = version.Id, UpdatedAtUtc = now, UpdatedByUserId = actor };
            db.AddRange(template, version, binding);
        }
        await db.SaveChangesAsync(ct);
    }
}
