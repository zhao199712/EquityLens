using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Data.Configurations;

public class RiskModelSettingConfiguration : IEntityTypeConfiguration<RiskModelSetting>
{
    public void Configure(EntityTypeBuilder<RiskModelSetting> builder)
    {
        builder.ToTable("risk_model_setting");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(64).IsRequired();
        builder.Property(x => x.LookbackDays).HasColumnName("lookback_days").HasDefaultValue(252);
        builder.Property(x => x.ConfidenceLevel).HasColumnName("confidence_level").HasPrecision(5, 4).HasDefaultValue(0.95m);
        builder.Property(x => x.HoldingPeriodDays).HasColumnName("holding_period_days").HasDefaultValue(1);
        builder.Property(x => x.Method).HasColumnName("method").HasMaxLength(32).HasDefaultValue("Historical");
        builder.Property(x => x.ReturnType).HasColumnName("return_type").HasMaxLength(16).HasDefaultValue("Log");
        builder.Property(x => x.IsDefault).HasColumnName("is_default").HasDefaultValue(false);
    }
}
