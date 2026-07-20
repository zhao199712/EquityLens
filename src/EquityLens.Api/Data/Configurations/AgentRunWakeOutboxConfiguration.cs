using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EquityLens.Api.Data.Configurations;

public sealed class AgentRunWakeOutboxConfiguration : IEntityTypeConfiguration<AgentRunWakeOutbox>
{
    public void Configure(EntityTypeBuilder<AgentRunWakeOutbox> builder)
    {
        builder.ToTable("agent_run_wake_outbox"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.AgentRunId).HasColumnName("agent_run_id");
        builder.Property(x => x.UserId).HasColumnName("user_id"); builder.Property(x => x.WorkflowType).HasColumnName("workflow_type").HasMaxLength(64);
        builder.Property(x => x.AgentRunNodeId).HasColumnName("agent_run_node_id"); builder.Property(x => x.DefinitionVersion).HasColumnName("definition_version");
        builder.Property(x => x.OrchestrationVersion).HasColumnName("orchestration_version"); builder.Property(x => x.CorrelationId).HasColumnName("correlation_id");
        builder.Property(x => x.CausationId).HasColumnName("causation_id"); builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(x => x.PublishedAtUtc).HasColumnName("published_at_utc"); builder.HasIndex(x => new { x.PublishedAtUtc, x.CreatedAtUtc });
    }
}
