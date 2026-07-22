using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Data.Configurations;

public class ResearchRunConfiguration : IEntityTypeConfiguration<ResearchRun>
{
    public void Configure(EntityTypeBuilder<ResearchRun> builder)
    {
        builder.ToTable("research_run");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.ParentResearchRunId).HasColumnName("parent_research_run_id");
        builder.Property(x => x.RevisionFeedbackId).HasColumnName("revision_feedback_id");
        builder.Property(x => x.TraceId).HasColumnName("trace_id").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Ticker).HasColumnName("ticker").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Question).HasColumnName("question").IsRequired();
        builder.Property(x => x.Answer).HasColumnName("answer").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Model).HasColumnName("model").HasMaxLength(128);
        builder.Property(x => x.RetrievalMode).HasColumnName("retrieval_mode").HasMaxLength(64).IsRequired();
        builder.Property(x => x.SourcePolicy).HasColumnName("source_policy").HasMaxLength(64).IsRequired();
        builder.Property(x => x.DocumentType).HasColumnName("document_type").HasMaxLength(64);
        builder.Property(x => x.TopK).HasColumnName("top_k");
        builder.Property(x => x.Temperature).HasColumnName("temperature");
        builder.Property(x => x.CitationCount).HasColumnName("citation_count");
        builder.Property(x => x.LatencyMs).HasColumnName("latency_ms");
        builder.Property(x => x.ErrorMessage).HasColumnName("error_message");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasDefaultValueSql("now()");

        builder.HasMany(x => x.Steps).WithOne(s => s.Run).HasForeignKey(s => s.RunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Candidates).WithOne(c => c.Run).HasForeignKey(c => c.RunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Citations).WithOne(c => c.Run).HasForeignKey(c => c.RunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.ParentResearchRun).WithMany(x => x.ChildResearchRuns).HasForeignKey(x => x.ParentResearchRunId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.Ticker, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.Status, x.CreatedAtUtc });
        builder.HasIndex(x => x.TraceId);
        builder.HasIndex(x => x.ParentResearchRunId);
        builder.HasIndex(x => x.RevisionFeedbackId).IsUnique();
    }
}
