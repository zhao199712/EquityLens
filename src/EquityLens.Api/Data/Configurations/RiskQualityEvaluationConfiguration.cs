using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EquityLens.Api.Data.Configurations;

public sealed class RiskQualityEvaluationConfiguration : IEntityTypeConfiguration<RiskQualityEvaluation>
{
    public void Configure(EntityTypeBuilder<RiskQualityEvaluation> builder)
    {
        builder.ToTable("risk_quality_evaluation");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.RiskCalculationRunId).HasColumnName("risk_calculation_run_id");
        builder.Property(x => x.RiskBacktestRunId).HasColumnName("risk_backtest_run_id");
        builder.Property(x => x.PolicyVersion).HasColumnName("policy_version").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Model).HasColumnName("model").HasMaxLength(128).IsRequired();
        builder.Property(x => x.ConfidenceLevel).HasColumnName("confidence_level").HasPrecision(8, 6);
        builder.Property(x => x.ObservationCount).HasColumnName("observation_count");
        builder.Property(x => x.KupiecPValue).HasColumnName("kupiec_p_value").HasPrecision(12, 10);
        builder.Property(x => x.ChristoffersenPValue).HasColumnName("christoffersen_p_value").HasPrecision(12, 10);
        builder.Property(x => x.EsStatus).HasColumnName("es_status").HasMaxLength(64);
        builder.Property(x => x.FitHealthy).HasColumnName("fit_healthy");
        builder.Property(x => x.FailureCodesJson).HasColumnName("failure_codes_json").HasColumnType("jsonb");
        builder.Property(x => x.WarningCodesJson).HasColumnName("warning_codes_json").HasColumnType("jsonb");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(x => x.EvaluatedAtUtc).HasColumnName("evaluated_at_utc");
        builder.HasIndex(x => new { x.RiskCalculationRunId, x.PolicyVersion }).IsUnique();
        builder.HasIndex(x => x.RiskBacktestRunId);
        builder.HasOne(x => x.RiskCalculationRun).WithMany(x => x.QualityEvaluations)
            .HasForeignKey(x => x.RiskCalculationRunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.RiskBacktestRun).WithMany()
            .HasForeignKey(x => x.RiskBacktestRunId).OnDelete(DeleteBehavior.Restrict);
    }
}
