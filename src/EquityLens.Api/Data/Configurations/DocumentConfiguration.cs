using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Data.Configurations;

public class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("document");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.UploadedFileId).HasColumnName("uploaded_file_id").IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(256).IsRequired();
        builder.Property(x => x.DocumentType).HasColumnName("document_type").HasMaxLength(32);
        builder.Property(x => x.SourceUrl).HasColumnName("source_url").HasMaxLength(1024);
        builder.Property(x => x.Language).HasColumnName("language").HasMaxLength(8).HasDefaultValue("en");
        builder.Property(x => x.PublishedAt).HasColumnName("published_at");
        builder.Property(x => x.ParsedAtUtc).HasColumnName("parsed_at_utc");
        builder.Property(x => x.ParseStatus).HasColumnName("parse_status").HasMaxLength(16).HasDefaultValue("Pending");

        builder.HasOne(x => x.UploadedFile)
            .WithMany(f => f.Documents)
            .HasForeignKey(x => x.UploadedFileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
