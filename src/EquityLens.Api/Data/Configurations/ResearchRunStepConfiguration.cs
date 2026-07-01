using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Data.Configurations;

public class ResearchRunStepConfiguration : IEntityTypeConfiguration<ResearchRunStep>
{
    public void Configure(EntityTypeBuilder<ResearchRunStep> builder)
    {
        builder.ToTable("research_run_step");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.RunId).HasColumnName("run_id").IsRequired();
        builder.Property(x => x.StepType).HasColumnName("step_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.InputJson).HasColumnName("input_json");
        builder.Property(x => x.OutputJson).HasColumnName("output_json");
        builder.Property(x => x.DurationMs).HasColumnName("duration_ms");
        builder.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc");
        builder.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");
        builder.Property(x => x.ErrorMessage).HasColumnName("error_message");

        builder.HasIndex(x => x.RunId);
    }
}
