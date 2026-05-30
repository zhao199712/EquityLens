using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pgvector;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Data.Configurations;

public class DocumentEmbeddingConfiguration : IEntityTypeConfiguration<DocumentEmbedding>
{
    public void Configure(EntityTypeBuilder<DocumentEmbedding> builder)
    {
        builder.ToTable("document_embedding");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.DocumentChunkId).HasColumnName("document_chunk_id").IsRequired();
        builder.Property(x => x.Embedding).HasColumnName("embedding").HasColumnType("vector(1536)");
        builder.Property(x => x.EmbeddingModel).HasColumnName("embedding_model").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Dimensions).HasColumnName("dimensions").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasDefaultValueSql("now()");

        builder.HasOne(x => x.DocumentChunk)
            .WithOne(c => c.Embedding)
            .HasForeignKey<DocumentEmbedding>(x => x.DocumentChunkId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.DocumentChunkId).IsUnique();
    }
}
