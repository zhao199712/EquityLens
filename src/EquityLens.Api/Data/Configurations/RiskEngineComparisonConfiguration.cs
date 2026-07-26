using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EquityLens.Api.Data.Configurations;

public sealed class RiskEngineComparisonConfiguration : IEntityTypeConfiguration<RiskEngineComparison>
{
    public void Configure(EntityTypeBuilder<RiskEngineComparison> builder)
    {
        builder.ToTable("risk_engine_comparison");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.RiskBacktestRunId).HasColumnName("risk_backtest_run_id").IsRequired();
        builder.Property(x => x.PrimaryEngine).HasColumnName("primary_engine").HasMaxLength(32).IsRequired();
        builder.Property(x => x.PrimaryAlgorithmVersion).HasColumnName("primary_algorithm_version").HasMaxLength(64).IsRequired();
        builder.Property(x => x.CandidateEngine).HasColumnName("candidate_engine").HasMaxLength(32).IsRequired();
        builder.Property(x => x.CandidateAlgorithmVersion).HasColumnName("candidate_algorithm_version").HasMaxLength(64).IsRequired();
        builder.Property(x => x.InputHash).HasColumnName("input_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(24).IsRequired();
        builder.Property(x => x.PrimaryDurationMs).HasColumnName("primary_duration_ms");
        builder.Property(x => x.CandidateDurationMs).HasColumnName("candidate_duration_ms");
        builder.Property(x => x.PassedTolerance).HasColumnName("passed_tolerance");
        builder.Property(x => x.ComparisonJson).HasColumnName("comparison_json").HasColumnType("jsonb");
        builder.Property(x => x.CandidateResultJson).HasColumnName("candidate_result_json").HasColumnType("jsonb");
        builder.Property(x => x.ErrorMessage).HasColumnName("error_message");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");
        builder.HasIndex(x => x.RiskBacktestRunId).IsUnique();
        builder.HasIndex(x => new { x.Status, x.CreatedAtUtc });
        builder.HasOne(x => x.RiskBacktestRun)
            .WithMany()
            .HasForeignKey(x => x.RiskBacktestRunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
