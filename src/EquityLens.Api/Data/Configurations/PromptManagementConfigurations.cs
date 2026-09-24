using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EquityLens.Api.Data.Configurations;

public sealed class PromptTemplateConfiguration : IEntityTypeConfiguration<PromptTemplate>
{
    public void Configure(EntityTypeBuilder<PromptTemplate> b)
    {
        b.ToTable("prompt_template"); b.HasKey(x => x.Id);
        b.Property(x => x.Key).HasColumnName("key").HasMaxLength(128).IsRequired(); b.HasIndex(x => x.Key).IsUnique();
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(256).IsRequired(); b.Property(x => x.Description).HasColumnName("description");
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        b.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc"); b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
        b.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc"); b.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id");
        b.Property(x => x.ConcurrencyToken).HasColumnName("concurrency_token").IsConcurrencyToken();
    }
}

public sealed class PromptVersionConfiguration : IEntityTypeConfiguration<PromptVersion>
{
    public void Configure(EntityTypeBuilder<PromptVersion> b)
    {
        b.ToTable("prompt_version"); b.HasKey(x => x.Id); b.HasIndex(x => new { x.PromptTemplateId, x.VersionNumber }).IsUnique();
        b.Property(x => x.PromptTemplateId).HasColumnName("prompt_template_id"); b.Property(x => x.VersionNumber).HasColumnName("version_number");
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired(); b.Property(x => x.SystemPrompt).HasColumnName("system_prompt").IsRequired();
        b.Property(x => x.UserPrompt).HasColumnName("user_prompt"); b.Property(x => x.RequiredVariablesJson).HasColumnName("required_variables_json").HasColumnType("jsonb").IsRequired();
        b.Property(x => x.ResponseFormat).HasColumnName("response_format").HasMaxLength(64); b.Property(x => x.ChangeSummary).HasColumnName("change_summary"); b.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(64).IsRequired();
        b.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc"); b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id"); b.Property(x => x.PublishedAtUtc).HasColumnName("published_at_utc"); b.Property(x => x.PublishedByUserId).HasColumnName("published_by_user_id"); b.Property(x => x.ConcurrencyToken).HasColumnName("concurrency_token").IsConcurrencyToken();
        b.HasOne(x => x.Template).WithMany(x => x.Versions).HasForeignKey(x => x.PromptTemplateId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PromptBindingConfiguration : IEntityTypeConfiguration<PromptBinding>
{
    public void Configure(EntityTypeBuilder<PromptBinding> b)
    {
        b.ToTable("prompt_binding"); b.HasKey(x => x.Id); b.HasIndex(x => x.UsageKey).IsUnique();
        b.Property(x => x.UsageKey).HasColumnName("usage_key").HasMaxLength(128).IsRequired(); b.Property(x => x.OwnerType).HasColumnName("owner_type").HasMaxLength(32).IsRequired(); b.Property(x => x.OwnerKey).HasColumnName("owner_key").HasMaxLength(128).IsRequired();
        b.Property(x => x.PromptTemplateId).HasColumnName("prompt_template_id"); b.Property(x => x.PromptVersionId).HasColumnName("prompt_version_id"); b.Property(x => x.IsActive).HasColumnName("is_active"); b.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc"); b.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id"); b.Property(x => x.ConcurrencyToken).HasColumnName("concurrency_token").IsConcurrencyToken();
        b.HasOne(x => x.Template).WithMany().HasForeignKey(x => x.PromptTemplateId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Version).WithMany(x => x.Bindings).HasForeignKey(x => x.PromptVersionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PromptAuditLogConfiguration : IEntityTypeConfiguration<PromptAuditLog>
{
    public void Configure(EntityTypeBuilder<PromptAuditLog> b)
    {
        b.ToTable("prompt_audit_log"); b.HasKey(x => x.Id); b.Property(x => x.Action).HasColumnName("action").HasMaxLength(64).IsRequired(); b.Property(x => x.RequestId).HasColumnName("request_id"); b.Property(x => x.Reason).HasColumnName("reason"); b.Property(x => x.MetadataJson).HasColumnName("metadata_json").HasColumnType("jsonb").IsRequired(); b.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc"); b.HasIndex(x => new { x.PromptTemplateId, x.CreatedAtUtc }); b.HasIndex(x => x.RequestId);
    }
}

public sealed class AgentRunPromptSnapshotConfiguration : IEntityTypeConfiguration<AgentRunPromptSnapshot>
{
    public void Configure(EntityTypeBuilder<AgentRunPromptSnapshot> b)
    {
        b.ToTable("agent_run_prompt_snapshot"); b.HasKey(x => x.Id); b.HasIndex(x => new { x.AgentRunId, x.UsageKey }).IsUnique();
        b.Property(x => x.AgentRunId).HasColumnName("agent_run_id"); b.Property(x => x.UsageKey).HasColumnName("usage_key").HasMaxLength(128).IsRequired(); b.Property(x => x.OwnerType).HasColumnName("owner_type").HasMaxLength(32).IsRequired(); b.Property(x => x.OwnerKey).HasColumnName("owner_key").HasMaxLength(128).IsRequired(); b.Property(x => x.PromptTemplateId).HasColumnName("prompt_template_id"); b.Property(x => x.PromptTemplateKey).HasColumnName("prompt_template_key").HasMaxLength(128).IsRequired(); b.Property(x => x.PromptVersionId).HasColumnName("prompt_version_id"); b.Property(x => x.PromptVersionNumber).HasColumnName("prompt_version_number"); b.Property(x => x.SystemPrompt).HasColumnName("system_prompt").IsRequired(); b.Property(x => x.UserPrompt).HasColumnName("user_prompt"); b.Property(x => x.RequiredVariablesJson).HasColumnName("required_variables_json").HasColumnType("jsonb").IsRequired(); b.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(64).IsRequired(); b.Property(x => x.ResolvedAtUtc).HasColumnName("resolved_at_utc"); b.HasOne(x => x.Run).WithMany(x => x.PromptSnapshots).HasForeignKey(x => x.AgentRunId).OnDelete(DeleteBehavior.Cascade);
    }
}
