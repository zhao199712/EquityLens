using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Data.Configurations;

public class AgentFeedbackConfiguration : IEntityTypeConfiguration<AgentFeedback>
{
    public void Configure(EntityTypeBuilder<AgentFeedback> builder)
    {
        builder.ToTable("agent_feedback");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.AgentRunId).HasColumnName("agent_run_id").IsRequired();
        builder.Property(x => x.AgentRunNodeId).HasColumnName("agent_run_node_id");
        builder.Property(x => x.FeedbackType).HasColumnName("feedback_type").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(16).HasDefaultValue("Requested");
        builder.Property(x => x.Prompt).HasColumnName("prompt").IsRequired();
        builder.Property(x => x.ResponseJson).HasColumnName("response_json").HasColumnType("jsonb");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasDefaultValueSql("now()");
        builder.Property(x => x.RespondedAtUtc).HasColumnName("responded_at_utc");

        builder.HasOne(x => x.AgentRun)
            .WithMany(r => r.Feedbacks)
            .HasForeignKey(x => x.AgentRunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.AgentRunNode)
            .WithMany()
            .HasForeignKey(x => x.AgentRunNodeId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.AgentRunId);
    }
}
