using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EquityLens.Api.Data.Configurations;

public class AgentRunConfiguration : IEntityTypeConfiguration<AgentRun>
{
    public void Configure(EntityTypeBuilder<AgentRun> builder)
    {
        builder.ToTable("agent_run");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.WorkflowType).HasColumnName("workflow_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.AgentType).HasColumnName("agent_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(64).IsRequired();
        builder.Property(x => x.InputJson).HasColumnName("input_json").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.OutputJson).HasColumnName("output_json").HasColumnType("jsonb");
        builder.Property(x => x.BlackboardJson).HasColumnName("blackboard_json").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.WorkflowDefinitionJson).HasColumnName("workflow_definition_json").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ErrorMessage).HasColumnName("error_message");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasDefaultValueSql("now()");
        builder.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc");
        builder.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");

        builder.HasMany(x => x.Nodes).WithOne(n => n.Run).HasForeignKey(n => n.AgentRunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Events).WithOne(e => e.Run).HasForeignKey(e => e.AgentRunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.ToolCalls).WithOne(t => t.Run).HasForeignKey(t => t.AgentRunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Feedback).WithOne(f => f.Run).HasForeignKey(f => f.AgentRunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AppUser>().WithMany(u => u.AgentRuns).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.WorkflowType, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.Status, x.CreatedAtUtc });
    }
}
