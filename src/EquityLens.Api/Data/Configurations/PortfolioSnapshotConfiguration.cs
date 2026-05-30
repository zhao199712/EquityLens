using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Data.Configurations;

public class PortfolioSnapshotConfiguration : IEntityTypeConfiguration<PortfolioSnapshot>
{
    public void Configure(EntityTypeBuilder<PortfolioSnapshot> builder)
    {
        builder.ToTable("portfolio_snapshot");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.PortfolioId).HasColumnName("portfolio_id").IsRequired();
        builder.Property(x => x.SnapshotDate).HasColumnName("snapshot_date").IsRequired();
        builder.Property(x => x.MarketValue).HasColumnName("market_value").HasPrecision(18, 4);
        builder.Property(x => x.CostValue).HasColumnName("cost_value").HasPrecision(18, 4);
        builder.Property(x => x.UnrealizedPnl).HasColumnName("unrealized_pnl").HasPrecision(18, 4);
        builder.Property(x => x.DailyReturn).HasColumnName("daily_return").HasPrecision(10, 6);
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).HasDefaultValue("USD");

        builder.HasOne(x => x.Portfolio)
            .WithMany(p => p.Snapshots)
            .HasForeignKey(x => x.PortfolioId)
            .OnDelete(DeleteBehavior.Cascade);

        // One snapshot per portfolio per day
        builder.HasIndex(x => new { x.PortfolioId, x.SnapshotDate }).IsUnique();
    }
}
