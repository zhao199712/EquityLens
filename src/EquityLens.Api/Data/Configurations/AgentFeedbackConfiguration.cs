using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

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
        builder.Property(x => x.ClientRequestId).HasColumnName("client_request_id").IsRequired();
        builder.Property(x => x.FollowUpAgentRunId).HasColumnName("follow_up_agent_run_id");
        builder.Property(x => x.FeedbackType).HasColumnName("feedback_type").HasMaxLength(128).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Prompt).HasColumnName("prompt").IsRequired();
        builder.Property(x => x.ResponseJson).HasColumnName("response_json").HasColumnType("jsonb");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasDefaultValueSql("now()");
        builder.Property(x => x.RespondedAtUtc).HasColumnName("responded_at_utc");

        builder.HasOne(x => x.Node).WithMany(n => n.Feedback).HasForeignKey(x => x.AgentRunNodeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.FollowUpRun).WithMany().HasForeignKey(x => x.FollowUpAgentRunId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(x => new { x.AgentRunId, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.AgentRunId, x.ClientRequestId }).IsUnique();
        builder.HasIndex(x => x.AgentRunNodeId);
    }
}
