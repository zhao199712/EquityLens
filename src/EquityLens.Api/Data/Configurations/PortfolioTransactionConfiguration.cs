using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Data.Configurations;

public class PortfolioTransactionConfiguration : IEntityTypeConfiguration<PortfolioTransaction>
{
    public void Configure(EntityTypeBuilder<PortfolioTransaction> builder)
    {
        builder.ToTable("portfolio_transaction");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.PortfolioId).HasColumnName("portfolio_id").IsRequired();
        builder.Property(x => x.SecurityId).HasColumnName("security_id").IsRequired();
        builder.Property(x => x.TransactionType).HasColumnName("transaction_type").HasMaxLength(4).IsRequired();
        builder.Property(x => x.Quantity).HasColumnName("quantity").HasColumnType("numeric(18,6)").IsRequired();
        builder.Property(x => x.Price).HasColumnName("price").HasColumnType("numeric(18,6)").IsRequired();
        builder.Property(x => x.Fee).HasColumnName("fee").HasColumnType("numeric(18,6)").HasDefaultValue(0m);
        builder.Property(x => x.TransactionDate).HasColumnName("transaction_date").IsRequired();
        builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(512);
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasDefaultValueSql("now()");

        builder.HasOne(x => x.Portfolio)
            .WithMany()
            .HasForeignKey(x => x.PortfolioId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Security)
            .WithMany()
            .HasForeignKey(x => x.SecurityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.PortfolioId, x.SecurityId, x.TransactionDate, x.TransactionType });
    }
}
