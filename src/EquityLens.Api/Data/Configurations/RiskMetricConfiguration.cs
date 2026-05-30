using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Data.Configurations;

public class RiskMetricConfiguration : IEntityTypeConfiguration<RiskMetric>
{
    public void Configure(EntityTypeBuilder<RiskMetric> builder)
    {
        builder.ToTable("risk_metric");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.RiskRunId).HasColumnName("risk_run_id").IsRequired();
        builder.Property(x => x.MetricName).HasColumnName("metric_name").HasMaxLength(32).IsRequired();
        builder.Property(x => x.MetricValue).HasColumnName("metric_value").HasPrecision(18, 6);
        builder.Property(x => x.Unit).HasColumnName("unit").HasMaxLength(16);
        builder.Property(x => x.ConfidenceLevel).HasColumnName("confidence_level").HasPrecision(5, 4);
        builder.Property(x => x.HoldingPeriodDays).HasColumnName("holding_period_days");

        builder.HasOne(x => x.RiskRun)
            .WithMany(r => r.Metrics)
            .HasForeignKey(x => x.RiskRunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
