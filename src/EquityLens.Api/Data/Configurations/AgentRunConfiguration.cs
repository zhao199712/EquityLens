using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EquityLens.Api.Data.Entities;

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
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(24).HasDefaultValue("Pending");
        builder.Property(x => x.InputJson).HasColumnName("input_json").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.OutputJson).HasColumnName("output_json").HasColumnType("jsonb");
        builder.Property(x => x.BlackboardJson).HasColumnName("blackboard_json").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.WorkflowDefinitionJson).HasColumnName("workflow_definition_json").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ErrorMessage).HasColumnName("error_message");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasDefaultValueSql("now()");
        builder.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc");
        builder.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");

        builder.HasOne(x => x.User)
            .WithMany(u => u.AgentRuns)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => new { x.WorkflowType, x.Status });
    }
}
