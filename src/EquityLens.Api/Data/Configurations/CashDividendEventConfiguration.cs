using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EquityLens.Api.Data.Configurations;

public sealed class CashDividendEventConfiguration : IEntityTypeConfiguration<CashDividendEvent>
{
    public void Configure(EntityTypeBuilder<CashDividendEvent> builder)
    {
        builder.ToTable("cash_dividend_event");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.SecurityId).HasColumnName("security_id").IsRequired();
        builder.Property(x => x.ExDividendDate).HasColumnName("ex_dividend_date").IsRequired();
        builder.Property(x => x.PaymentDate).HasColumnName("payment_date");
        builder.Property(x => x.CashAmountPerShare).HasColumnName("cash_amount_per_share").HasPrecision(18, 6);
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.Source).HasColumnName("source").HasMaxLength(32).IsRequired();
        builder.Property(x => x.SourceKey).HasColumnName("source_key").HasMaxLength(256).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").HasDefaultValueSql("now()");
        builder.HasIndex(x => new { x.Source, x.SourceKey }).IsUnique();
        builder.HasOne(x => x.Security).WithMany().HasForeignKey(x => x.SecurityId).OnDelete(DeleteBehavior.Cascade);
    }
}
