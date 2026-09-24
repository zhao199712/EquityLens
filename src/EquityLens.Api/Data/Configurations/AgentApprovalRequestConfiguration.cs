using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EquityLens.Api.Data.Configurations;

public sealed class AgentApprovalRequestConfiguration : IEntityTypeConfiguration<AgentApprovalRequest>
{
    public void Configure(EntityTypeBuilder<AgentApprovalRequest> builder)
    {
        builder.ToTable("agent_approval_request");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.AgentRunId).HasColumnName("agent_run_id");
        builder.Property(x => x.AgentRunNodeId).HasColumnName("agent_run_node_id");
        builder.Property(x => x.NodeKey).HasColumnName("node_key").HasMaxLength(128).IsRequired();
        builder.Property(x => x.NodeType).HasColumnName("node_type").HasMaxLength(128).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired().IsConcurrencyToken();
        builder.Property(x => x.SideEffectLevel).HasColumnName("side_effect_level").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.PolicySnapshotJson).HasColumnName("policy_snapshot_json").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ExecutionAttempt).HasColumnName("execution_attempt");
        builder.Property(x => x.RequestedAtUtc).HasColumnName("requested_at_utc");
        builder.Property(x => x.DecidedAtUtc).HasColumnName("decided_at_utc");
        builder.Property(x => x.ConsumedAtUtc).HasColumnName("consumed_at_utc");
        builder.Property(x => x.DecidedByUserId).HasColumnName("decided_by_user_id");
        builder.Property(x => x.ClientRequestId).HasColumnName("client_request_id");
        builder.Property(x => x.DecisionComment).HasColumnName("decision_comment").HasMaxLength(2000);

        builder.HasOne(x => x.Run).WithMany(x => x.Approvals).HasForeignKey(x => x.AgentRunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Node).WithMany(x => x.Approvals).HasForeignKey(x => x.AgentRunNodeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(x => x.DecidedByUserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(x => new { x.AgentRunId, x.RequestedAtUtc });
        builder.HasIndex(x => new { x.AgentRunNodeId, x.ExecutionAttempt, x.Status })
            .IsUnique()
            .HasFilter("status = 'Pending'");
        builder.HasIndex(x => x.ClientRequestId)
            .IsUnique()
            .HasFilter("client_request_id IS NOT NULL");
    }
}
