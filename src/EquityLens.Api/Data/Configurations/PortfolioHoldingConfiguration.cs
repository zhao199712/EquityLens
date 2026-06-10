using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Data.Configurations;

public class PortfolioHoldingConfiguration : IEntityTypeConfiguration<PortfolioHolding>
{
    public void Configure(EntityTypeBuilder<PortfolioHolding> builder)
    {
        builder.ToTable("portfolio_holding");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.PortfolioId).HasColumnName("portfolio_id").IsRequired();
        builder.Property(x => x.SecurityId).HasColumnName("security_id").IsRequired();
        builder.Property(x => x.Quantity).HasColumnName("quantity").HasPrecision(18, 6);
        builder.Property(x => x.AverageCost).HasColumnName("average_cost").HasPrecision(18, 6);
        builder.Property(x => x.CostCurrency).HasColumnName("cost_currency").HasMaxLength(3).HasDefaultValue("USD");
        builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(512);
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").HasDefaultValueSql("now()");

        builder.HasOne(x => x.Portfolio)
            .WithMany(p => p.Holdings)
            .HasForeignKey(x => x.PortfolioId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Security)
            .WithMany(s => s.Holdings)
            .HasForeignKey(x => x.SecurityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.PortfolioId, x.SecurityId }).IsUnique();
    }
}
