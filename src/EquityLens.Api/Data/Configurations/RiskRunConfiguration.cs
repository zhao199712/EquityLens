using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Data.Configurations;

public class RiskRunConfiguration : IEntityTypeConfiguration<RiskRun>
{
    public void Configure(EntityTypeBuilder<RiskRun> builder)
    {
        builder.ToTable("risk_run");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.PortfolioId).HasColumnName("portfolio_id").IsRequired();
        builder.Property(x => x.RiskModelSettingId).HasColumnName("risk_model_setting_id").IsRequired();
        builder.Property(x => x.RunName).HasColumnName("run_name").HasMaxLength(128);
        builder.Property(x => x.AsOfDate).HasColumnName("as_of_date").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(16).HasDefaultValue("Pending");
        builder.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc");
        builder.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");
        builder.Property(x => x.ErrorMessage).HasColumnName("error_message");
        builder.Property(x => x.InputHash).HasColumnName("input_hash").HasMaxLength(64);

        builder.HasOne(x => x.Portfolio)
            .WithMany(p => p.RiskRuns)
            .HasForeignKey(x => x.PortfolioId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.RiskModelSetting)
            .WithMany(s => s.RiskRuns)
            .HasForeignKey(x => x.RiskModelSettingId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
