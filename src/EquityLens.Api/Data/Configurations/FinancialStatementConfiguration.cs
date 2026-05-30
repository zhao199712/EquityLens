using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Data.Configurations;

public class FinancialStatementConfiguration : IEntityTypeConfiguration<FinancialStatement>
{
    public void Configure(EntityTypeBuilder<FinancialStatement> builder)
    {
        builder.ToTable("financial_statement");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.SecurityId).HasColumnName("security_id").IsRequired();
        builder.Property(x => x.StatementType).HasColumnName("statement_type").HasMaxLength(32).IsRequired();
        builder.Property(x => x.PeriodType).HasColumnName("period_type").HasMaxLength(16).IsRequired();
        builder.Property(x => x.FiscalYear).HasColumnName("fiscal_year").IsRequired();
        builder.Property(x => x.FiscalQuarter).HasColumnName("fiscal_quarter");
        builder.Property(x => x.PeriodEndDate).HasColumnName("period_end_date").IsRequired();
        builder.Property(x => x.PublishedDate).HasColumnName("published_date");
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).HasDefaultValue("USD");
        builder.Property(x => x.DataSource).HasColumnName("data_source").HasMaxLength(32);

        builder.HasOne(x => x.Security)
            .WithMany(s => s.FinancialStatements)
            .HasForeignKey(x => x.SecurityId)
            .OnDelete(DeleteBehavior.Cascade);

        // Unique constraint: one statement per type per period per security
        builder.HasIndex(x => new { x.SecurityId, x.StatementType, x.PeriodType, x.FiscalYear, x.FiscalQuarter })
            .IsUnique();
    }
}
