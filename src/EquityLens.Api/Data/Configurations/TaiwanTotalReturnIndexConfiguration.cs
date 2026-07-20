using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EquityLens.Api.Data.Configurations;
public sealed class TaiwanTotalReturnIndexConfiguration : IEntityTypeConfiguration<TaiwanTotalReturnIndex>
{
    public void Configure(EntityTypeBuilder<TaiwanTotalReturnIndex> builder)
    {
        builder.ToTable("taiwan_total_return_index");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.TradingDate).HasColumnName("trading_date").IsRequired();
        builder.Property(x => x.Value).HasColumnName("value").HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.Source).HasColumnName("source").HasMaxLength(32).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").HasDefaultValueSql("now()");
        builder.HasIndex(x => x.TradingDate).IsUnique();
    }
}
