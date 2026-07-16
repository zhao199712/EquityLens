using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EquityLens.Api.Data.Configurations;

public sealed class PortfolioCashFlowConfiguration : IEntityTypeConfiguration<PortfolioCashFlow>
{
    public void Configure(EntityTypeBuilder<PortfolioCashFlow> builder)
    {
        builder.ToTable("portfolio_cash_flow");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.PortfolioId).HasColumnName("portfolio_id").IsRequired();
        builder.Property(x => x.SecurityId).HasColumnName("security_id");
        builder.Property(x => x.CashDividendEventId).HasColumnName("cash_dividend_event_id");
        builder.Property(x => x.FlowType).HasColumnName("flow_type").HasMaxLength(24).IsRequired();
        builder.Property(x => x.Amount).HasColumnName("amount").HasPrecision(18, 6);
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.EffectiveDate).HasColumnName("effective_date").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
        builder.Property(x => x.IsUserAdjusted).HasColumnName("is_user_adjusted").HasDefaultValue(false);
        builder.Property(x => x.IsSystemDerived).HasColumnName("is_system_derived").HasDefaultValue(false);
        builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(512);
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasDefaultValueSql("now()");
        builder.HasIndex(x => new { x.PortfolioId, x.EffectiveDate });
        builder.HasIndex(x => new { x.PortfolioId, x.CashDividendEventId }).IsUnique();
        builder.HasOne(x => x.Portfolio).WithMany(x => x.CashFlows).HasForeignKey(x => x.PortfolioId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Security).WithMany().HasForeignKey(x => x.SecurityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CashDividendEvent).WithMany().HasForeignKey(x => x.CashDividendEventId).OnDelete(DeleteBehavior.Cascade);
    }
}
