namespace EquityLens.Api.Contracts.Agents;

public sealed record PromptTemplateResponse(Guid Id, string Key, string Name, string? Description, string Status, DateTime UpdatedAtUtc, Guid ConcurrencyToken, IReadOnlyList<PromptVersionResponse> Versions);
public sealed record PromptVersionResponse(Guid Id, int VersionNumber, string Status, string RequiredVariablesJson, string? ResponseFormat, string? ChangeSummary, string ContentHash, DateTime CreatedAtUtc, DateTime? PublishedAtUtc, Guid ConcurrencyToken);
public sealed record PromptVersionContentResponse(Guid Id, string SystemPrompt, string? UserPrompt, string RequiredVariablesJson, string? ResponseFormat, string ContentHash);
public sealed record PromptBindingResponse(Guid Id, string UsageKey, string OwnerType, string OwnerKey, Guid PromptTemplateId, string PromptTemplateKey, Guid PromptVersionId, int PromptVersionNumber, bool IsActive, DateTime UpdatedAtUtc, Guid ConcurrencyToken);
public sealed record PromptAuditLogResponse(Guid Id, string Action, Guid ActorUserId, Guid RequestId, string? Reason, string MetadataJson, DateTime CreatedAtUtc);
public sealed record AgentRunPromptSnapshotResponse(Guid Id, string UsageKey, string OwnerType, string OwnerKey, string PromptTemplateKey, Guid PromptVersionId, int PromptVersionNumber, string ContentHash, DateTime ResolvedAtUtc);
public sealed record AgentRunPromptSnapshotContentResponse(AgentRunPromptSnapshotResponse Snapshot, string SystemPrompt, string? UserPrompt, string RequiredVariablesJson);

public sealed record CreatePromptTemplateRequest(Guid RequestId, string Key, string Name, string? Description, string SystemPrompt, string? UserPrompt, string RequiredVariablesJson, string? ResponseFormat, string? ChangeSummary);
public sealed record CreatePromptVersionRequest(Guid RequestId, string SystemPrompt, string? UserPrompt, string RequiredVariablesJson, string? ResponseFormat, string? ChangeSummary);
public sealed record UpdatePromptVersionRequest(Guid RequestId, Guid ConcurrencyToken, string SystemPrompt, string? UserPrompt, string RequiredVariablesJson, string? ResponseFormat, string? ChangeSummary);
public sealed record PublishPromptVersionRequest(Guid RequestId, Guid ConcurrencyToken, string? Reason, IReadOnlyList<string> ActivateUsageKeys);
public sealed record RollbackPromptVersionRequest(Guid RequestId, Guid ConcurrencyToken, string? Reason, IReadOnlyList<string> ActivateUsageKeys);
public sealed record DisablePromptTemplateRequest(Guid RequestId, Guid ConcurrencyToken, string? Reason);
public sealed record UpdatePromptBindingRequest(Guid RequestId, Guid ConcurrencyToken, Guid PromptVersionId, bool IsActive, string? Reason);
