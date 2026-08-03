using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EquityLens.Api.Contracts.Agents;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.Agents;

public sealed class PromptManagementException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public interface IPromptManagementService
{
    Task<IReadOnlyList<PromptTemplateResponse>> ListTemplatesAsync(CancellationToken ct = default);
    Task<PromptTemplateResponse?> GetTemplateAsync(Guid templateId, CancellationToken ct = default);
    Task<PromptVersionContentResponse?> GetVersionContentAsync(Guid versionId, CancellationToken ct = default);
    Task<PromptTemplateResponse> CreateTemplateAsync(CreatePromptTemplateRequest request, Guid actorId, CancellationToken ct = default);
    Task<PromptVersionResponse> CreateDraftAsync(Guid templateId, CreatePromptVersionRequest request, Guid actorId, CancellationToken ct = default);
    Task<PromptVersionResponse> UpdateDraftAsync(Guid versionId, UpdatePromptVersionRequest request, Guid actorId, CancellationToken ct = default);
    Task<PromptVersionResponse> PublishAsync(Guid versionId, PublishPromptVersionRequest request, Guid actorId, CancellationToken ct = default);
    Task<PromptVersionResponse> RollbackAsync(Guid versionId, RollbackPromptVersionRequest request, Guid actorId, CancellationToken ct = default);
    Task DisableTemplateAsync(Guid templateId, DisablePromptTemplateRequest request, Guid actorId, CancellationToken ct = default);
    Task<IReadOnlyList<PromptBindingResponse>> ListBindingsAsync(CancellationToken ct = default);
    Task<PromptBindingResponse?> UpdateBindingAsync(string usageKey, UpdatePromptBindingRequest request, Guid actorId, CancellationToken ct = default);
    Task<IReadOnlyList<PromptAuditLogResponse>> ListAuditAsync(Guid? templateId, CancellationToken ct = default);
    Task<IReadOnlyList<AgentRunPromptSnapshotResponse>> ListRunSnapshotsAsync(Guid runId, CancellationToken ct = default);
    Task<AgentRunPromptSnapshotContentResponse?> GetRunSnapshotContentAsync(Guid runId, Guid snapshotId, CancellationToken ct = default);
}

/// <summary>管理可版本化 prompt、綁定及稽核；所有 mutation 都在一個交易中完成。</summary>
public sealed class PromptManagementService(EquityLensDbContext db) : IPromptManagementService
{
    public async Task<IReadOnlyList<PromptTemplateResponse>> ListTemplatesAsync(CancellationToken ct = default) =>
        (await db.PromptTemplates.AsNoTracking().Include(x => x.Versions).OrderBy(x => x.Key).ToListAsync(ct)).Select(Map).ToList();

    public async Task<PromptTemplateResponse?> GetTemplateAsync(Guid templateId, CancellationToken ct = default)
    {
        var entity = await db.PromptTemplates.AsNoTracking().Include(x => x.Versions).SingleOrDefaultAsync(x => x.Id == templateId, ct);
        return entity is null ? null : Map(entity);
    }

    public async Task<PromptVersionContentResponse?> GetVersionContentAsync(Guid versionId, CancellationToken ct = default)
    {
        var v = await db.PromptVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == versionId, ct);
        return v is null ? null : new(v.Id, v.SystemPrompt, v.UserPrompt, v.RequiredVariablesJson, v.ResponseFormat, v.ContentHash);
    }

    public async Task<PromptTemplateResponse> CreateTemplateAsync(CreatePromptTemplateRequest r, Guid actorId, CancellationToken ct = default)
    {
        ValidateKey(r.Key); ValidateVariables(r.RequiredVariablesJson); EnsureRequestId(r.RequestId);
        if (await db.PromptAuditLogs.AnyAsync(x => x.RequestId == r.RequestId, ct)) throw new PromptManagementException("prompt_request_replayed", "此 RequestId 已經處理過。");
        var now = DateTime.UtcNow; var template = new PromptTemplate { Id = Guid.NewGuid(), Key = r.Key.Trim(), Name = r.Name.Trim(), Description = r.Description?.Trim(), CreatedAtUtc = now, UpdatedAtUtc = now, CreatedByUserId = actorId, UpdatedByUserId = actorId };
        var version = NewVersion(template.Id, 1, r.SystemPrompt, r.UserPrompt, r.RequiredVariablesJson, r.ResponseFormat, r.ChangeSummary, actorId);
        db.AddRange(template, version); Audit("TemplateCreated", actorId, r.RequestId, template.Id, version.Id, null, null, new { template.Key }); await db.SaveChangesAsync(ct); return Map(template);
    }

    public async Task<PromptVersionResponse> CreateDraftAsync(Guid templateId, CreatePromptVersionRequest r, Guid actorId, CancellationToken ct = default)
    {
        ValidateVariables(r.RequiredVariablesJson); EnsureRequestId(r.RequestId); var t = await db.PromptTemplates.Include(x => x.Versions).SingleOrDefaultAsync(x => x.Id == templateId, ct) ?? throw new PromptManagementException("prompt_not_found", "Prompt template 不存在。");
        EnsureActive(t); var v = NewVersion(t.Id, t.Versions.DefaultIfEmpty().Max(x => x?.VersionNumber ?? 0) + 1, r.SystemPrompt, r.UserPrompt, r.RequiredVariablesJson, r.ResponseFormat, r.ChangeSummary, actorId); db.Add(v); Audit("DraftCreated", actorId, r.RequestId, t.Id, v.Id, null, null, new { v.VersionNumber }); await db.SaveChangesAsync(ct); return Map(v);
    }

    public async Task<PromptVersionResponse> UpdateDraftAsync(Guid versionId, UpdatePromptVersionRequest r, Guid actorId, CancellationToken ct = default)
    {
        ValidateVariables(r.RequiredVariablesJson); var v = await db.PromptVersions.SingleOrDefaultAsync(x => x.Id == versionId, ct) ?? throw new PromptManagementException("prompt_not_found", "Prompt version 不存在。"); EnsureDraft(v); EnsureConcurrency(v.ConcurrencyToken, r.ConcurrencyToken); v.SystemPrompt = r.SystemPrompt; v.UserPrompt = r.UserPrompt; v.RequiredVariablesJson = r.RequiredVariablesJson; v.ResponseFormat = r.ResponseFormat; v.ChangeSummary = r.ChangeSummary; v.ContentHash = Hash(v.SystemPrompt, v.UserPrompt, v.RequiredVariablesJson, v.ResponseFormat); v.ConcurrencyToken = Guid.NewGuid(); Audit("DraftUpdated", actorId, r.RequestId, v.PromptTemplateId, v.Id, null, null, new { v.VersionNumber }); await db.SaveChangesAsync(ct); return Map(v);
    }

    public Task<PromptVersionResponse> PublishAsync(Guid versionId, PublishPromptVersionRequest r, Guid actorId, CancellationToken ct = default) => ChangePublishedStateAsync(versionId, r.RequestId, r.ConcurrencyToken, r.Reason, r.ActivateUsageKeys, actorId, "Published");
    public Task<PromptVersionResponse> RollbackAsync(Guid versionId, RollbackPromptVersionRequest r, Guid actorId, CancellationToken ct = default) => ChangePublishedStateAsync(versionId, r.RequestId, r.ConcurrencyToken, r.Reason, r.ActivateUsageKeys, actorId, "RolledBack");

    private async Task<PromptVersionResponse> ChangePublishedStateAsync(Guid versionId, Guid requestId, Guid token, string? reason, IReadOnlyList<string> usageKeys, Guid actorId, string auditAction)
    {
        EnsureRequestId(requestId); await using var tx = await db.Database.BeginTransactionAsync();
        var v = await db.PromptVersions.Include(x => x.Template).SingleOrDefaultAsync(x => x.Id == versionId) ?? throw new PromptManagementException("prompt_not_found", "Prompt version 不存在。"); EnsureActive(v.Template); EnsureConcurrency(v.ConcurrencyToken, token); if (v.Status is not ("Draft" or "Retired")) throw new PromptManagementException("prompt_version_not_editable", "只有 Draft 或 Retired version 可以發布／回滾。");
        var previous = await db.PromptVersions.Where(x => x.PromptTemplateId == v.PromptTemplateId && x.Status == "Published").ToListAsync(); foreach (var old in previous) { old.Status = "Retired"; old.ConcurrencyToken = Guid.NewGuid(); }
        v.Status = "Published"; v.PublishedAtUtc = DateTime.UtcNow; v.PublishedByUserId = actorId; v.ConcurrencyToken = Guid.NewGuid();
        foreach (var key in usageKeys.Distinct(StringComparer.Ordinal)) { var b = await db.PromptBindings.SingleOrDefaultAsync(x => x.UsageKey == key) ?? throw new PromptManagementException("prompt_binding_conflict", $"找不到 usage key: {key}"); b.PromptTemplateId = v.PromptTemplateId; b.PromptVersionId = v.Id; b.IsActive = true; b.UpdatedAtUtc = DateTime.UtcNow; b.UpdatedByUserId = actorId; b.ConcurrencyToken = Guid.NewGuid(); }
        Audit(auditAction, actorId, requestId, v.PromptTemplateId, v.Id, null, reason, new { activatedUsageKeys = usageKeys }); await db.SaveChangesAsync(); await tx.CommitAsync(); return Map(v);
    }

    public async Task DisableTemplateAsync(Guid templateId, DisablePromptTemplateRequest r, Guid actorId, CancellationToken ct = default)
    {
        var t = await db.PromptTemplates.SingleOrDefaultAsync(x => x.Id == templateId, ct) ?? throw new PromptManagementException("prompt_not_found", "Prompt template 不存在。"); EnsureConcurrency(t.ConcurrencyToken, r.ConcurrencyToken); if (await db.PromptBindings.AnyAsync(x => x.PromptTemplateId == templateId && x.IsActive, ct)) throw new PromptManagementException("prompt_binding_conflict", "仍有 active binding 的 template 不可停用。"); t.Status = "Disabled"; t.UpdatedAtUtc = DateTime.UtcNow; t.UpdatedByUserId = actorId; t.ConcurrencyToken = Guid.NewGuid(); Audit("TemplateDisabled", actorId, r.RequestId, t.Id, null, null, r.Reason, new { }); await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PromptBindingResponse>> ListBindingsAsync(CancellationToken ct = default) => (await db.PromptBindings.AsNoTracking().Include(x => x.Template).Include(x => x.Version).OrderBy(x => x.UsageKey).ToListAsync(ct)).Select(Map).ToList();
    public async Task<PromptBindingResponse?> UpdateBindingAsync(string usageKey, UpdatePromptBindingRequest r, Guid actorId, CancellationToken ct = default)
    {
        var b = await db.PromptBindings.Include(x => x.Template).Include(x => x.Version).SingleOrDefaultAsync(x => x.UsageKey == usageKey, ct); if (b is null) return null; EnsureConcurrency(b.ConcurrencyToken, r.ConcurrencyToken);
        var target = await db.PromptVersions.Include(x => x.Template).SingleAsync(x => x.Id == r.PromptVersionId, ct); if (target.Status is not ("Published" or "Retired") || target.Template.Status != "Active") throw new PromptManagementException("prompt_binding_conflict", "只能綁定 active template 的 Published 或 Retired version。");
        b.PromptTemplateId = target.PromptTemplateId; b.PromptVersionId = target.Id; b.IsActive = r.IsActive; b.UpdatedAtUtc = DateTime.UtcNow; b.UpdatedByUserId = actorId; b.ConcurrencyToken = Guid.NewGuid(); await db.SaveChangesAsync(ct); Audit("BindingUpdated", actorId, r.RequestId, target.PromptTemplateId, target.Id, b.Id, r.Reason, new { usageKey }); await db.SaveChangesAsync(ct); return Map(b);
    }
    public async Task<IReadOnlyList<PromptAuditLogResponse>> ListAuditAsync(Guid? templateId, CancellationToken ct = default) => (await db.PromptAuditLogs.AsNoTracking().Where(x => !templateId.HasValue || x.PromptTemplateId == templateId).OrderByDescending(x => x.CreatedAtUtc).Take(500).ToListAsync(ct)).Select(x => new PromptAuditLogResponse(x.Id, x.Action, x.ActorUserId, x.RequestId, x.Reason, x.MetadataJson, x.CreatedAtUtc)).ToList();
    public async Task<IReadOnlyList<AgentRunPromptSnapshotResponse>> ListRunSnapshotsAsync(Guid runId, CancellationToken ct = default) => (await db.AgentRunPromptSnapshots.AsNoTracking().Where(x => x.AgentRunId == runId).OrderBy(x => x.UsageKey).ToListAsync(ct)).Select(Map).ToList();
    public async Task<AgentRunPromptSnapshotContentResponse?> GetRunSnapshotContentAsync(Guid runId, Guid snapshotId, CancellationToken ct = default) { var s = await db.AgentRunPromptSnapshots.AsNoTracking().SingleOrDefaultAsync(x => x.AgentRunId == runId && x.Id == snapshotId, ct); return s is null ? null : new(Map(s), s.SystemPrompt, s.UserPrompt, s.RequiredVariablesJson); }

    private static PromptVersion NewVersion(Guid templateId, int number, string system, string? user, string variables, string? format, string? summary, Guid actor) => new() { Id = Guid.NewGuid(), PromptTemplateId = templateId, VersionNumber = number, SystemPrompt = system, UserPrompt = user, RequiredVariablesJson = variables, ResponseFormat = format, ChangeSummary = summary, ContentHash = Hash(system, user, variables, format), CreatedByUserId = actor };
    private void Audit(string action, Guid actor, Guid requestId, Guid? templateId, Guid? versionId, Guid? bindingId, string? reason, object metadata) => db.PromptAuditLogs.Add(new() { Id = Guid.NewGuid(), Action = action, ActorUserId = actor, RequestId = requestId, PromptTemplateId = templateId, PromptVersionId = versionId, PromptBindingId = bindingId, Reason = reason, MetadataJson = JsonSerializer.Serialize(metadata) });
    private static string Hash(params string?[] values) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\u001f", values)))).ToLowerInvariant();
    private static void ValidateKey(string key) { if (string.IsNullOrWhiteSpace(key) || key.Length > 128 || key.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '_'))) throw new PromptManagementException("prompt_variable_contract_invalid", "Prompt key 格式無效。"); }
    private static void ValidateVariables(string json) { try { using var d = JsonDocument.Parse(json); if (d.RootElement.ValueKind != JsonValueKind.Array || d.RootElement.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String)) throw new JsonException(); } catch (JsonException) { throw new PromptManagementException("prompt_variable_contract_invalid", "requiredVariables 必須為字串陣列 JSON。"); } }
    private static void EnsureRequestId(Guid id) { if (id == Guid.Empty) throw new PromptManagementException("prompt_variable_contract_invalid", "RequestId 必填。"); }
    private static void EnsureConcurrency(Guid actual, Guid expected) { if (actual != expected) throw new PromptManagementException("prompt_concurrency_conflict", "資料已被其他管理者更新，請重新載入。"); }
    private static void EnsureDraft(PromptVersion v) { if (v.Status != "Draft") throw new PromptManagementException("prompt_version_not_editable", "只有 Draft version 可以編輯。"); }
    private static void EnsureActive(PromptTemplate t) { if (t.Status != "Active") throw new PromptManagementException("prompt_binding_conflict", "Prompt template 已停用。"); }
    private static PromptTemplateResponse Map(PromptTemplate x) => new(x.Id, x.Key, x.Name, x.Description, x.Status, x.UpdatedAtUtc, x.ConcurrencyToken, x.Versions.OrderByDescending(v => v.VersionNumber).Select(Map).ToList());
    private static PromptVersionResponse Map(PromptVersion x) => new(x.Id, x.VersionNumber, x.Status, x.RequiredVariablesJson, x.ResponseFormat, x.ChangeSummary, x.ContentHash, x.CreatedAtUtc, x.PublishedAtUtc, x.ConcurrencyToken);
    private static PromptBindingResponse Map(PromptBinding x) => new(x.Id, x.UsageKey, x.OwnerType, x.OwnerKey, x.PromptTemplateId, x.Template.Key, x.PromptVersionId, x.Version.VersionNumber, x.IsActive, x.UpdatedAtUtc, x.ConcurrencyToken);
    private static AgentRunPromptSnapshotResponse Map(AgentRunPromptSnapshot x) => new(x.Id, x.UsageKey, x.OwnerType, x.OwnerKey, x.PromptTemplateKey, x.PromptVersionId, x.PromptVersionNumber, x.ContentHash, x.ResolvedAtUtc);
}
