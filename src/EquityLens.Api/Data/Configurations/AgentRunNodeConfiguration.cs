using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EquityLens.Api.Data.Configurations;

public class AgentRunNodeConfiguration : IEntityTypeConfiguration<AgentRunNode>
{
    public void Configure(EntityTypeBuilder<AgentRunNode> builder)
    {
        builder.ToTable("agent_run_node");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.AgentRunId).HasColumnName("agent_run_id").IsRequired();
        builder.Property(x => x.NodeKey).HasColumnName("node_key").HasMaxLength(128).IsRequired();
        builder.Property(x => x.NodeType).HasColumnName("node_type").HasMaxLength(128).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(64).IsRequired();
        builder.Property(x => x.InputJson).HasColumnName("input_json").HasColumnType("jsonb");
        builder.Property(x => x.OutputJson).HasColumnName("output_json").HasColumnType("jsonb");
        builder.Property(x => x.ErrorMessage).HasColumnName("error_message");
        builder.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc");
        builder.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");
        builder.Property(x => x.DurationMs).HasColumnName("duration_ms");

        builder.HasIndex(x => x.AgentRunId);
        builder.HasIndex(x => new { x.AgentRunId, x.NodeKey }).IsUnique();
    }
}
