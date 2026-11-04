using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EquityLens.Api.Data.Configurations;

public sealed class UserRiskProfileConfiguration : IEntityTypeConfiguration<UserRiskProfile>
{
    public void Configure(EntityTypeBuilder<UserRiskProfile> b)
    {
        b.ToTable("user_risk_profile");
        b.HasKey(x => x.UserId);
        b.Property(x => x.UserId).HasColumnName("user_id");
        b.HasOne<AppUser>().WithOne().HasForeignKey<UserRiskProfile>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Property(x => x.Level).HasColumnName("level").HasMaxLength(16);
        b.Property(x => x.Source).HasColumnName("source").HasMaxLength(16);
        b.Property(x => x.CapacityLevel).HasColumnName("capacity_level").HasMaxLength(16);
        b.Property(x => x.WillingnessLevel).HasColumnName("willingness_level").HasMaxLength(16);
        b.Property(x => x.CapacityScore).HasColumnName("capacity_score");
        b.Property(x => x.WillingnessScore).HasColumnName("willingness_score");
        b.Property(x => x.QuestionnaireVersion).HasColumnName("questionnaire_version").HasMaxLength(32);
        b.Property(x => x.ThresholdVersion).HasColumnName("threshold_version").HasMaxLength(32);
        b.Property(x => x.AnswersJson).HasColumnName("answers_json").HasColumnType("jsonb");
        b.Property(x => x.AssessedAtUtc).HasColumnName("assessed_at_utc");
    }
}
