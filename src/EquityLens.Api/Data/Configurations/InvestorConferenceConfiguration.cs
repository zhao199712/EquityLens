using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Data.Configurations;

public class InvestorConferenceConfiguration : IEntityTypeConfiguration<InvestorConference>
{
    public void Configure(EntityTypeBuilder<InvestorConference> builder)
    {
        builder.ToTable("investor_conference");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.SecurityId).HasColumnName("security_id").IsRequired();
        builder.Property(x => x.UploadedFileId).HasColumnName("uploaded_file_id");
        builder.Property(x => x.DocumentId).HasColumnName("document_id");

        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(256).IsRequired();
        builder.Property(x => x.EventDate).HasColumnName("event_date");
        builder.Property(x => x.Location).HasColumnName("location").HasMaxLength(256);
        builder.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(2048);
        builder.Property(x => x.Language).HasColumnName("language").HasMaxLength(8).HasDefaultValue("zh-TW");
        builder.Property(x => x.Source).HasColumnName("source").HasMaxLength(64);
        builder.Property(x => x.SourceUrl).HasColumnName("source_url").HasMaxLength(1024);
        builder.Property(x => x.OriginalFileName).HasColumnName("original_file_name").HasMaxLength(256);
        builder.Property(x => x.OriginalFileUrl).HasColumnName("original_file_url").HasMaxLength(1024);

        builder.Property(x => x.ParseStatus).HasColumnName("parse_status").HasMaxLength(16).HasDefaultValue("Pending");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasDefaultValueSql("now()");

        // FKs
        builder.HasOne(x => x.Security)
            .WithMany(s => s.InvestorConferences)
            .HasForeignKey(x => x.SecurityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.UploadedFile)
            .WithMany(f => f.InvestorConferences)
            .HasForeignKey(x => x.UploadedFileId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.Document)
            .WithMany()
            .HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.SetNull);

        // Index by security for fast lookup
        builder.HasIndex(x => new { x.SecurityId, x.EventDate });
    }
}
