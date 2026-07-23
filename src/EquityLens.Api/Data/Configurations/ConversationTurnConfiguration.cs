using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EquityLens.Api.Data.Configurations;

public sealed class ConversationTurnConfiguration : IEntityTypeConfiguration<ConversationTurn>
{
    public void Configure(EntityTypeBuilder<ConversationTurn> builder)
    {
        builder.ToTable("conversation_turn");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.ChatSessionId).HasColumnName("chat_session_id").IsRequired();
        builder.Property(x => x.RequestId).HasColumnName("request_id").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Action).HasColumnName("action").HasMaxLength(32);
        builder.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(64);
        builder.Property(x => x.StandaloneQuery).HasColumnName("standalone_query").HasMaxLength(2000);
        builder.Property(x => x.InputContextVersion).HasColumnName("input_context_version");
        builder.Property(x => x.OutputContextVersion).HasColumnName("output_context_version");
        builder.Property(x => x.AgentRunId).HasColumnName("agent_run_id");
        builder.Property(x => x.ResearchRunId).HasColumnName("research_run_id");
        builder.Property(x => x.Model).HasColumnName("model").HasMaxLength(128);
        builder.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(64);
        builder.Property(x => x.PromptTemplateId).HasColumnName("prompt_template_id").HasMaxLength(128);
        builder.Property(x => x.PromptVersion).HasColumnName("prompt_version");
        builder.Property(x => x.PromptTokens).HasColumnName("prompt_tokens");
        builder.Property(x => x.CompletionTokens).HasColumnName("completion_tokens");
        builder.Property(x => x.DurationMs).HasColumnName("duration_ms");
        builder.Property(x => x.ErrorMessage).HasColumnName("error_message");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasDefaultValueSql("now()");
        builder.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");
        builder.HasOne(x => x.ChatSession).WithMany(x => x.Turns).HasForeignKey(x => x.ChatSessionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.AgentRun).WithMany().HasForeignKey(x => x.AgentRunId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(x => new { x.ChatSessionId, x.RequestId }).IsUnique();
        builder.HasIndex(x => x.AgentRunId);
    }
}
