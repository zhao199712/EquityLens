using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Data.Configurations;

public class ResearchRunCandidateConfiguration : IEntityTypeConfiguration<ResearchRunCandidate>
{
    public void Configure(EntityTypeBuilder<ResearchRunCandidate> builder)
    {
        builder.ToTable("research_run_candidate");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.RunId).HasColumnName("run_id").IsRequired();
        builder.Property(x => x.SearchId).HasColumnName("search_id").HasMaxLength(64);
        builder.Property(x => x.Query).HasColumnName("query");
        builder.Property(x => x.DocumentChunkId).HasColumnName("document_chunk_id").IsRequired();
        builder.Property(x => x.DocumentId).HasColumnName("document_id");
        builder.Property(x => x.Title).HasColumnName("title");
        builder.Property(x => x.DocumentType).HasColumnName("document_type").HasMaxLength(64);
        builder.Property(x => x.SourceRole).HasColumnName("source_role").HasMaxLength(64);
        builder.Property(x => x.PageNumber).HasColumnName("page_number");
        builder.Property(x => x.RelevanceScore).HasColumnName("relevance_score");
        builder.Property(x => x.AdjustedScore).HasColumnName("adjusted_score");
        builder.Property(x => x.RankBeforeRerank).HasColumnName("rank_before_rerank");
        builder.Property(x => x.RankAfterRerank).HasColumnName("rank_after_rerank");
        builder.Property(x => x.Decision).HasColumnName("decision").HasMaxLength(64).IsRequired();
        builder.Property(x => x.DiscardReason).HasColumnName("discard_reason");
        builder.Property(x => x.ContentPreview).HasColumnName("content_preview");

        builder.HasIndex(x => x.RunId);
        builder.HasIndex(x => x.DocumentChunkId);
    }
}
