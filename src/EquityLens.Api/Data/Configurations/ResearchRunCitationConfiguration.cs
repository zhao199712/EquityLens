using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Data.Configurations;

public class ResearchRunCitationConfiguration : IEntityTypeConfiguration<ResearchRunCitation>
{
    public void Configure(EntityTypeBuilder<ResearchRunCitation> builder)
    {
        builder.ToTable("research_run_citation");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.RunId).HasColumnName("run_id").IsRequired();
        builder.Property(x => x.CitationIndex).HasColumnName("citation_index");
        builder.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.DocumentChunkId).HasColumnName("document_chunk_id");
        builder.Property(x => x.DocumentId).HasColumnName("document_id");
        builder.Property(x => x.Title).HasColumnName("title");
        builder.Property(x => x.DocumentType).HasColumnName("document_type").HasMaxLength(64);
        builder.Property(x => x.SourceRole).HasColumnName("source_role").HasMaxLength(64);
        builder.Property(x => x.PageNumber).HasColumnName("page_number");
        builder.Property(x => x.QuoteText).HasColumnName("quote_text");
        builder.Property(x => x.RelevanceScore).HasColumnName("relevance_score");

        builder.HasIndex(x => x.RunId);
        builder.HasIndex(x => x.DocumentChunkId);
    }
}
