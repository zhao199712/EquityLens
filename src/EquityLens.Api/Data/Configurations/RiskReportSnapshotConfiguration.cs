using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EquityLens.Api.Data.Configurations;

public sealed class RiskReportSnapshotConfiguration : IEntityTypeConfiguration<RiskReportSnapshot>
{
    public void Configure(EntityTypeBuilder<RiskReportSnapshot> builder)
    {
        builder.ToTable("risk_report_snapshot");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.PortfolioId).HasColumnName("portfolio_id");
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(x => x.DataAsOfDate).HasColumnName("data_as_of_date");
        builder.Property(x => x.Model).HasColumnName("model").HasMaxLength(64);
        builder.Property(x => x.ThresholdVersion).HasColumnName("threshold_version").HasMaxLength(32);
        builder.Property(x => x.SnapshotJson).HasColumnName("snapshot_json").HasColumnType("jsonb");
        builder.HasIndex(x => new { x.PortfolioId, x.CreatedAtUtc });
        builder.HasOne(x => x.Portfolio).WithMany().HasForeignKey(x => x.PortfolioId).OnDelete(DeleteBehavior.Cascade);
    }
}
