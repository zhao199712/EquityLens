using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EquityLens.Api.Data.Configurations;

public class AgentRunEventConfiguration : IEntityTypeConfiguration<AgentRunEvent>
{
    public void Configure(EntityTypeBuilder<AgentRunEvent> builder)
    {
        builder.ToTable("agent_run_event");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.AgentRunId).HasColumnName("agent_run_id").IsRequired();
        builder.Property(x => x.AgentRunNodeId).HasColumnName("agent_run_node_id");
        builder.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Message).HasColumnName("message");
        builder.Property(x => x.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasDefaultValueSql("now()");

        builder.HasOne(x => x.Node).WithMany(n => n.Events).HasForeignKey(x => x.AgentRunNodeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.AgentRunId, x.CreatedAtUtc });
        builder.HasIndex(x => x.AgentRunNodeId);
    }
}
