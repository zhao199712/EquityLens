using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Data.Configurations;

public class SecurityConfiguration : IEntityTypeConfiguration<Security>
{
    public void Configure(EntityTypeBuilder<Security> builder)
    {
        builder.ToTable("security");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.Ticker).HasColumnName("ticker").HasMaxLength(16).IsRequired();
        builder.Property(x => x.Exchange).HasColumnName("exchange").HasMaxLength(16).IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(256).IsRequired();
        builder.Property(x => x.AssetType).HasColumnName("asset_type").HasMaxLength(32);
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).HasDefaultValue("USD");
        builder.Property(x => x.Isin).HasColumnName("isin").HasMaxLength(12);
        builder.Property(x => x.Sector).HasColumnName("sector").HasMaxLength(64);
        builder.Property(x => x.Industry).HasColumnName("industry").HasMaxLength(64);
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(x => x.MetadataUpdatedAtUtc).HasColumnName("metadata_updated_at_utc");
        builder.Property(x => x.MetadataSource).HasColumnName("metadata_source").HasMaxLength(32);
        builder.Property(x => x.PricesSyncedAtUtc).HasColumnName("prices_synced_at_utc");
        builder.Property(x => x.PricesSource).HasColumnName("prices_source").HasMaxLength(32);

        // Unique constraint on (Ticker, Exchange)
        builder.HasIndex(x => new { x.Ticker, x.Exchange }).IsUnique();
    }
}
