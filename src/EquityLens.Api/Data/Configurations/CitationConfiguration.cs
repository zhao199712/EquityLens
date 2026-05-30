using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Data.Configurations;

public class CitationConfiguration : IEntityTypeConfiguration<Citation>
{
    public void Configure(EntityTypeBuilder<Citation> builder)
    {
        builder.ToTable("citation");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.AiMemoId).HasColumnName("ai_memo_id").IsRequired();
        builder.Property(x => x.DocumentChunkId).HasColumnName("document_chunk_id");
        builder.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(32);
        builder.Property(x => x.SourceTitle).HasColumnName("source_title").HasMaxLength(256);
        builder.Property(x => x.SourceUrl).HasColumnName("source_url").HasMaxLength(1024);
        builder.Property(x => x.ReferenceKey).HasColumnName("reference_key").HasMaxLength(64);
        builder.Property(x => x.QuoteText).HasColumnName("quote_text");
        builder.Property(x => x.RelevanceScore).HasColumnName("relevance_score").HasPrecision(5, 4);
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasDefaultValueSql("now()");

        builder.HasOne(x => x.AiMemo)
            .WithMany(m => m.Citations)
            .HasForeignKey(x => x.AiMemoId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.DocumentChunk)
            .WithMany(c => c.Citations)
            .HasForeignKey(x => x.DocumentChunkId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
