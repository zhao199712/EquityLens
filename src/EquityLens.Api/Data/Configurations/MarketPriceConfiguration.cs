using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Data.Configurations;

public class MarketPriceConfiguration : IEntityTypeConfiguration<MarketPrice>
{
    public void Configure(EntityTypeBuilder<MarketPrice> builder)
    {
        builder.ToTable("market_price");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.SecurityId).HasColumnName("security_id").IsRequired();
        builder.Property(x => x.PriceTime).HasColumnName("price_time").IsRequired();
        builder.Property(x => x.Interval).HasColumnName("interval").HasMaxLength(8).HasDefaultValue("1d");
        builder.Property(x => x.Open).HasColumnName("open").HasPrecision(18, 6);
        builder.Property(x => x.High).HasColumnName("high").HasPrecision(18, 6);
        builder.Property(x => x.Low).HasColumnName("low").HasPrecision(18, 6);
        builder.Property(x => x.Close).HasColumnName("close").HasPrecision(18, 6);
        builder.Property(x => x.AdjustedClose).HasColumnName("adjusted_close").HasPrecision(18, 6);
        builder.Property(x => x.Volume).HasColumnName("volume");
        builder.Property(x => x.DataSource).HasColumnName("data_source").HasMaxLength(32);
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasDefaultValueSql("now()");

        builder.HasOne(x => x.Security)
            .WithMany(s => s.Prices)
            .HasForeignKey(x => x.SecurityId)
            .OnDelete(DeleteBehavior.Cascade);

        // Index for querying price history
        builder.HasIndex(x => new { x.SecurityId, x.Interval, x.PriceTime });
    }
}
