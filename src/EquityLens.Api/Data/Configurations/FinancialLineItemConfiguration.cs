using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Data.Configurations;

public class FinancialLineItemConfiguration : IEntityTypeConfiguration<FinancialLineItem>
{
    public void Configure(EntityTypeBuilder<FinancialLineItem> builder)
    {
        builder.ToTable("financial_line_item");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.FinancialStatementId).HasColumnName("financial_statement_id").IsRequired();
        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
        builder.Property(x => x.Amount).HasColumnName("amount").HasPrecision(18, 4);
        builder.Property(x => x.Unit).HasColumnName("unit").HasMaxLength(16);

        builder.HasOne(x => x.FinancialStatement)
            .WithMany(s => s.LineItems)
            .HasForeignKey(x => x.FinancialStatementId)
            .OnDelete(DeleteBehavior.Cascade);

        // Unique constraint: one code per statement
        builder.HasIndex(x => new { x.FinancialStatementId, x.Code }).IsUnique();
    }
}
