using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Data.Configurations;

public class PortfolioTransactionConfiguration : IEntityTypeConfiguration<PortfolioTransaction>
{
    public void Configure(EntityTypeBuilder<PortfolioTransaction> builder)
    {
        builder.ToTable("portfolio_transaction", table =>
        {
            table.HasCheckConstraint(
                "ck_portfolio_transaction_transaction_type",
                "transaction_type IN ('BUY', 'SELL')");
            table.HasCheckConstraint(
                "ck_portfolio_transaction_quantity_positive",
                "quantity > 0");
            table.HasCheckConstraint(
                "ck_portfolio_transaction_price_non_negative",
                "price >= 0");
            table.HasCheckConstraint(
                "ck_portfolio_transaction_fee_non_negative",
                "fee >= 0");
        });

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
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128);
        builder.Property(x => x.RequestHash).HasColumnName("request_hash").HasMaxLength(64);
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
        builder.HasIndex(x => new { x.PortfolioId, x.IdempotencyKey })
            .IsUnique();
    }
}
