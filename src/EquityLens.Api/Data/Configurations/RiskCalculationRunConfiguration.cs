using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EquityLens.Api.Data.Configurations;

public sealed class RiskCalculationRunConfiguration : IEntityTypeConfiguration<RiskCalculationRun>
{
    public void Configure(EntityTypeBuilder<RiskCalculationRun> builder)
    {
        builder.ToTable("risk_calculation_run");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.PortfolioId).HasColumnName("portfolio_id").IsRequired();
        builder.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id").IsRequired();
        builder.Property(x => x.Operation).HasColumnName("operation").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(24).IsRequired();
        builder.Property(x => x.ProgressPercent).HasColumnName("progress_percent");
        builder.Property(x => x.RequestedModel).HasColumnName("requested_model").HasMaxLength(128).IsRequired();
        builder.Property(x => x.SelectedModel).HasColumnName("selected_model").HasMaxLength(128);
        builder.Property(x => x.AlgorithmVersion).HasColumnName("algorithm_version").HasMaxLength(64).IsRequired();
        builder.Property(x => x.InputHash).HasColumnName("input_hash").HasMaxLength(64);
        builder.Property(x => x.InputSnapshotJson).HasColumnName("input_snapshot_json").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ResultJson).HasColumnName("result_json").HasColumnType("jsonb");
        builder.Property(x => x.FitHealthJson).HasColumnName("fit_health_json").HasColumnType("jsonb");
        builder.Property(x => x.DataFactorVersion).HasColumnName("data_factor_version").HasMaxLength(128);
        builder.Property(x => x.FallbackReason).HasColumnName("fallback_reason").HasMaxLength(256);
        builder.Property(x => x.FallbackDepth).HasColumnName("fallback_depth");
        builder.Property(x => x.ErrorCode).HasColumnName("error_code").HasMaxLength(128);
        builder.Property(x => x.ErrorMessage).HasColumnName("error_message");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc");
        builder.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");
        builder.HasIndex(x => new { x.PortfolioId, x.CreatedAtUtc });
        builder.HasIndex(x => x.InputHash);
        builder.HasOne(x => x.Portfolio).WithMany().HasForeignKey(x => x.PortfolioId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
