using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Data.Configurations;

public class AgentToolCallConfiguration : IEntityTypeConfiguration<AgentToolCall>
{
    public void Configure(EntityTypeBuilder<AgentToolCall> builder)
    {
        builder.ToTable("agent_tool_call");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.AgentRunId).HasColumnName("agent_run_id").IsRequired();
        builder.Property(x => x.AgentRunNodeId).HasColumnName("agent_run_node_id");
        builder.Property(x => x.ToolName).HasColumnName("tool_name").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(16).HasDefaultValue("Running");
        builder.Property(x => x.ArgumentsJson).HasColumnName("arguments_json").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ResultPreview).HasColumnName("result_preview");
        builder.Property(x => x.ResultJson).HasColumnName("result_json").HasColumnType("jsonb");
        builder.Property(x => x.ErrorMessage).HasColumnName("error_message");
        builder.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc");
        builder.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");
        builder.Property(x => x.DurationMs).HasColumnName("duration_ms");

        builder.HasOne(x => x.AgentRun)
            .WithMany(r => r.ToolCalls)
            .HasForeignKey(x => x.AgentRunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.AgentRunNode)
            .WithMany()
            .HasForeignKey(x => x.AgentRunNodeId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.AgentRunId);
    }
}
