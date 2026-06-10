using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Data.Configurations;

public class FinancialFilingConfiguration : IEntityTypeConfiguration<FinancialFiling>
{
    public void Configure(EntityTypeBuilder<FinancialFiling> builder)
    {
        builder.ToTable("financial_filing");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.SecurityId).HasColumnName("security_id").IsRequired();
        builder.Property(x => x.UploadedFileId).HasColumnName("uploaded_file_id").IsRequired();
        builder.Property(x => x.DocumentId).HasColumnName("document_id");
        builder.Property(x => x.UploadedByUserId).HasColumnName("uploaded_by_user_id").IsRequired();

        builder.Property(x => x.FilingType).HasColumnName("filing_type").HasMaxLength(32).IsRequired();
        builder.Property(x => x.FiscalYear).HasColumnName("fiscal_year").IsRequired();
        builder.Property(x => x.FiscalQuarter).HasColumnName("fiscal_quarter");
        builder.Property(x => x.PeriodEndDate).HasColumnName("period_end_date");
        builder.Property(x => x.PublishedAt).HasColumnName("published_at");
        builder.Property(x => x.Language).HasColumnName("language").HasMaxLength(8).HasDefaultValue("en");
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3);
        builder.Property(x => x.Source).HasColumnName("source").HasMaxLength(64);
        builder.Property(x => x.SourceUrl).HasColumnName("source_url").HasMaxLength(1024);
        builder.Property(x => x.ParseStatus).HasColumnName("parse_status").HasMaxLength(16).HasDefaultValue("Pending");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasDefaultValueSql("now()");

        // FKs
        builder.HasOne(x => x.Security)
            .WithMany(s => s.FinancialFilings)
            .HasForeignKey(x => x.SecurityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.UploadedFile)
            .WithMany(f => f.FinancialFilings)
            .HasForeignKey(x => x.UploadedFileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Document)
            .WithMany()
            .HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.UploadedByUser)
            .WithMany(u => u.FinancialFilings)
            .HasForeignKey(x => x.UploadedByUserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Index by security for fast lookup
        builder.HasIndex(x => new { x.SecurityId, x.FiscalYear, x.FiscalQuarter, x.FilingType });
    }
}
