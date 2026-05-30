using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Data.Configurations;

public class CriticNoteConfiguration : IEntityTypeConfiguration<CriticNote>
{
    public void Configure(EntityTypeBuilder<CriticNote> builder)
    {
        builder.ToTable("critic_note");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.AiMemoId).HasColumnName("ai_memo_id").IsRequired();
        builder.Property(x => x.ReviewerType).HasColumnName("reviewer_type").HasMaxLength(16).IsRequired();
        builder.Property(x => x.ReviewerName).HasColumnName("reviewer_name").HasMaxLength(64);
        builder.Property(x => x.Severity).HasColumnName("severity").HasMaxLength(16).HasDefaultValue("Info");
        builder.Property(x => x.Comment).HasColumnName("comment").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasDefaultValueSql("now()");

        builder.HasOne(x => x.AiMemo)
            .WithMany(m => m.CriticNotes)
            .HasForeignKey(x => x.AiMemoId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
