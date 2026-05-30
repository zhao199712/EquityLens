using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Data.Configurations;

public class ScenarioResultConfiguration : IEntityTypeConfiguration<ScenarioResult>
{
    public void Configure(EntityTypeBuilder<ScenarioResult> builder)
    {
        builder.ToTable("scenario_result");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.RiskRunId).HasColumnName("risk_run_id").IsRequired();
        builder.Property(x => x.ScenarioId).HasColumnName("scenario_id").IsRequired();
        builder.Property(x => x.PortfolioValueBefore).HasColumnName("portfolio_value_before").HasPrecision(18, 4);
        builder.Property(x => x.PortfolioValueAfter).HasColumnName("portfolio_value_after").HasPrecision(18, 4);
        builder.Property(x => x.PnlAmount).HasColumnName("pnl_amount").HasPrecision(18, 4);
        builder.Property(x => x.PnlPercent).HasColumnName("pnl_percent").HasPrecision(10, 6);

        builder.HasOne(x => x.RiskRun)
            .WithMany(r => r.ScenarioResults)
            .HasForeignKey(x => x.RiskRunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Scenario)
            .WithMany(s => s.ScenarioResults)
            .HasForeignKey(x => x.ScenarioId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
