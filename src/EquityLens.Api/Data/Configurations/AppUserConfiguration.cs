using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Data.Configurations;

public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("app_user");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.Email).HasColumnName("email").HasMaxLength(256).IsRequired();
        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(128).IsRequired();
        builder.Property(x => x.PasswordHash).HasColumnName("password_hash").HasMaxLength(512);
        builder.Property(x => x.GoogleSubject).HasColumnName("google_subject").HasMaxLength(255);
        builder.Property(x => x.Role).HasColumnName("role").HasMaxLength(32).HasDefaultValue("User");
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").HasDefaultValueSql("now()");

        builder.HasIndex(x => x.Email).IsUnique();
        builder.HasIndex(x => x.GoogleSubject).IsUnique().HasFilter("google_subject IS NOT NULL");
    }
}
