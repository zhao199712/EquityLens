using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EquityLens.Api.Data.Configurations;

public sealed class PriceAdjustmentBatchConfiguration : IEntityTypeConfiguration<PriceAdjustmentBatch>
{
    public void Configure(EntityTypeBuilder<PriceAdjustmentBatch> b)
    {
        b.ToTable("price_adjustment_batch");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.SecurityId).HasColumnName("security_id");
        b.Property(x => x.FetchedAtUtc).HasColumnName("fetched_at_utc");
        b.Property(x => x.From).HasColumnName("coverage_from");
        b.Property(x => x.To).HasColumnName("coverage_to");
        b.Property(x => x.VerifiedThrough).HasColumnName("verified_through");
        b.Property(x => x.Source).HasColumnName("source").HasMaxLength(128);
        b.Property(x => x.AlgorithmVersion).HasColumnName("algorithm_version").HasMaxLength(64);
        b.Property(x => x.SnapshotJson).HasColumnName("snapshot_json").HasColumnType("text");
        b.Property(x => x.SnapshotHash).HasColumnName("snapshot_hash").HasMaxLength(64);
        b.Property(x => x.SeriesHash).HasColumnName("series_hash").HasMaxLength(64);
        b.HasOne<Security>().WithMany().HasForeignKey(x => x.SecurityId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.SecurityId, x.FetchedAtUtc });
    }
}
