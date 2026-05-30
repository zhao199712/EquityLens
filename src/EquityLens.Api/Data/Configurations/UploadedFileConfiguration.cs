using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Data.Configurations;

public class UploadedFileConfiguration : IEntityTypeConfiguration<UploadedFile>
{
    public void Configure(EntityTypeBuilder<UploadedFile> builder)
    {
        builder.ToTable("uploaded_file");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.UploadedByUserId).HasColumnName("uploaded_by_user_id").IsRequired();
        builder.Property(x => x.BucketName).HasColumnName("bucket_name").HasMaxLength(128).IsRequired();
        builder.Property(x => x.ObjectKey).HasColumnName("object_key").HasMaxLength(512).IsRequired();
        builder.Property(x => x.OriginalFileName).HasColumnName("original_file_name").HasMaxLength(256).IsRequired();
        builder.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(128);
        builder.Property(x => x.FileSizeBytes).HasColumnName("file_size_bytes");
        builder.Property(x => x.Sha256Hash).HasColumnName("sha256_hash").HasMaxLength(64);
        builder.Property(x => x.StorageProvider).HasColumnName("storage_provider").HasMaxLength(32).HasDefaultValue("MinIO");
        builder.Property(x => x.UploadStatus).HasColumnName("upload_status").HasMaxLength(16).HasDefaultValue("Pending");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasDefaultValueSql("now()");

        builder.HasOne(x => x.UploadedByUser)
            .WithMany(u => u.UploadedFiles)
            .HasForeignKey(x => x.UploadedByUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.ObjectKey).IsUnique();
    }
}
