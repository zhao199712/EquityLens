using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Data.Configurations;

public class JobRunConfiguration : IEntityTypeConfiguration<JobRun>
{
    public void Configure(EntityTypeBuilder<JobRun> builder)
    {
        builder.ToTable("job_run");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(x => x.RiskRunId).HasColumnName("risk_run_id");
        builder.Property(x => x.FinancialReportId).HasColumnName("financial_report_id");
        builder.Property(x => x.UploadedFileId).HasColumnName("uploaded_file_id");
        builder.Property(x => x.JobType).HasColumnName("job_type").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(16).HasDefaultValue("Queued");
        builder.Property(x => x.ProgressPercent).HasColumnName("progress_percent").HasDefaultValue(0);
        builder.Property(x => x.RedisJobId).HasColumnName("redis_job_id").HasMaxLength(64);
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(64);
        builder.Property(x => x.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb");
        builder.Property(x => x.ResultJson).HasColumnName("result_json").HasColumnType("jsonb");
        builder.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc");
        builder.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");
        builder.Property(x => x.ErrorMessage).HasColumnName("error_message");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasDefaultValueSql("now()").IsRequired();

        builder.HasOne(x => x.CreatedByUser)
            .WithMany(u => u.JobRuns)
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.RiskRun)
            .WithMany(r => r.JobRuns)
            .HasForeignKey(x => x.RiskRunId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.FinancialReport)
            .WithMany(r => r.JobRuns)
            .HasForeignKey(x => x.FinancialReportId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.UploadedFile)
            .WithMany(f => f.JobRuns)
            .HasForeignKey(x => x.UploadedFileId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
