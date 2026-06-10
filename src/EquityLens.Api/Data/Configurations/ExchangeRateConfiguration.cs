using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Data.Configurations;

public class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate>
{
    public void Configure(EntityTypeBuilder<ExchangeRate> builder)
    {
        builder.ToTable("exchange_rate");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.SourceCurrency).HasColumnName("source_currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.TargetCurrency).HasColumnName("target_currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.Rate).HasColumnName("rate").HasColumnType("numeric(18,6)").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").HasDefaultValueSql("now()");

        builder.HasIndex(x => new { x.SourceCurrency, x.TargetCurrency }).IsUnique();
    }
}
