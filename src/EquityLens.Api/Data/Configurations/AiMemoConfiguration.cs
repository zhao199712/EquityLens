using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Data.Configurations;

public class AiMemoConfiguration : IEntityTypeConfiguration<AiMemo>
{
    public void Configure(EntityTypeBuilder<AiMemo> builder)
    {
        builder.ToTable("ai_memo");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.RiskRunId).HasColumnName("risk_run_id");
        builder.Property(x => x.FinancialReportId).HasColumnName("financial_report_id");
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(256).IsRequired();
        builder.Property(x => x.Content).HasColumnName("content").IsRequired();
        builder.Property(x => x.ModelName).HasColumnName("model_name").HasMaxLength(64);
        builder.Property(x => x.PromptVersion).HasColumnName("prompt_version").HasMaxLength(32);
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasDefaultValueSql("now()");

        builder.HasOne(x => x.RiskRun)
            .WithMany(r => r.AiMemos)
            .HasForeignKey(x => x.RiskRunId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.FinancialReport)
            .WithMany(r => r.AiMemos)
            .HasForeignKey(x => x.FinancialReportId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
