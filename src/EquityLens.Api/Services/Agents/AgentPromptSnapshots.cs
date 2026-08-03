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
        new("agent.router.investment-research", "Agent", "InvestmentResearchRouter", "投研問題路由", "投研 workflow 與 lead skill 路由。"),
        new("agent.planner.dynamic-workflow", "Agent", "LlmAgentWorkflowPlanner", "動態工作流規劃", "規劃受限的動態 DAG。"),
        new("agent.skill.conference-call-takeaways", "Skill", "conference-call-takeaways", "法說會重點", "法說會重點整理。"),
        new("agent.capability.web-request", "Agent", "LlmWebCapabilityRequestAgent", "Web 能力請求", "判斷是否需要 Web 證據。"),
        new("agent.critic.review", "Agent", "LlmCriticReviewAgent", "評論答案", "檢查答案和證據品質。"),
        new("agent.revision.draft", "Agent", "LlmDraftRevisionAgent", "修訂答案", "依評論修訂答案。"),
        new("agent.portfolio.narrative", "Agent", "LlmPortfolioDiagnosisNarrativeAgent", "投組診斷敘事", "依已驗證資料撰寫投組診斷。"),
        new("agent.evidence.retrieval-plan", "Agent", "EvidenceRetrievalPlanAgent", "證據檢索規劃", "規劃補充證據檢索。"),
        new("agent.evidence.claim-extraction", "Agent", "LlmEvidenceRemediationAgent", "Claim 擷取", "自答案擷取可驗證 claims。"),
        new("agent.evidence.revision", "Agent", "LlmEvidenceRemediationAgent", "證據修訂", "以驗證證據修訂答案。"),
        new("agent.evidence.assessor", "Agent", "LlmEvidenceAssessor", "證據評估", "評估 claim 與證據支持。"),
        new("agent.evidence.reanalysis", "Agent", "LlmInvestmentReanalysisAgent", "重新分析", "依新證據重新分析結論。"),
        new("agent.research.answer", "Workflow", AgentWorkflowTypes.ResearchInvestigation, "研究答案生成", "以已選研究證據生成帶引用答案。")
    ];

    public static IReadOnlyList<PromptUsageDefinition> ForWorkflow(string workflowType) => workflowType switch
    {
        AgentWorkflowTypes.CriticReview => [Find("agent.critic.review")],
        AgentWorkflowTypes.DraftRevision => [Find("agent.revision.draft")],
        AgentWorkflowTypes.PortfolioDiagnosis => [Find("agent.portfolio.narrative")],
        AgentWorkflowTypes.EvidenceRemediation => [Find("agent.evidence.retrieval-plan"), Find("agent.evidence.claim-extraction"), Find("agent.evidence.revision"), Find("agent.evidence.assessor")],
        AgentWorkflowTypes.EvidenceReanalysis => [Find("agent.evidence.reanalysis"), Find("agent.critic.review"), Find("agent.evidence.revision")],
        AgentWorkflowTypes.ResearchInvestigation or AgentWorkflowTypes.ResearchQualityReview or AgentWorkflowTypes.FeedbackRevision => [Find("agent.planner.dynamic-workflow"), Find("agent.capability.web-request"), Find("agent.research.answer"), Find("agent.skill.conference-call-takeaways")],
        _ => []
    };
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
