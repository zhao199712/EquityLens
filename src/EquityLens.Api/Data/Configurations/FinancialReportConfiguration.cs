using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Data.Configurations;

public class FinancialReportConfiguration : IEntityTypeConfiguration<FinancialReport>
{
    public void Configure(EntityTypeBuilder<FinancialReport> builder)
    {
        builder.ToTable("financial_report");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.PortfolioId).HasColumnName("portfolio_id").IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(256).IsRequired();
        builder.Property(x => x.AsOfDate).HasColumnName("as_of_date").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(16).HasDefaultValue("Draft");
        builder.Property(x => x.Summary).HasColumnName("summary");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasDefaultValueSql("now()");

        builder.HasOne(x => x.Portfolio)
            .WithMany(p => p.FinancialReports)
            .HasForeignKey(x => x.PortfolioId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
