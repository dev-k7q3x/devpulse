using DevPulse.Domain.Entities;
using DevPulse.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevPulse.Infrastructure.Persistence.Configurations;

internal sealed class UserSettingsConfiguration : IEntityTypeConfiguration<UserSettings>
{
    public void Configure(EntityTypeBuilder<UserSettings> builder)
    {
        builder.ToTable("user_settings", table =>
        {
            table.HasCheckConstraint("ck_user_settings_send_hour", $"send_hour BETWEEN {SendHour.Min} AND {SendHour.Max}");
            table.HasCheckConstraint(
                "ck_user_settings_pending_email_token",
                "(pending_email IS NULL) = (pending_email_token_hash IS NULL)");
        });

        builder.HasKey(x => x.UserId);
        builder.HasOne<User>().WithOne().HasForeignKey<UserSettings>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        builder.Property(x => x.Email).HasMaxLength(UserSettings.EmailMaxLength);
        builder.Property(x => x.EmailConfirmed).HasDatabaseDefault(false);
        builder.Property(x => x.PendingEmail).HasMaxLength(UserSettings.EmailMaxLength);
        builder.Property(x => x.PendingEmailTokenHash).HasColumnType("bytea");
        builder.Property(x => x.SendHour)
            .HasConversion(hour => (short)hour.Value, value => new SendHour(value))
            .HasColumnType("smallint")
            .HasDatabaseDefault(SendHour.Default);
        builder.Property(x => x.TimeZoneId).HasMaxLength(UserSettings.TimeZoneIdMaxLength).HasDatabaseDefault(UserSettings.DefaultTimeZoneId);
        builder.Property(x => x.DigestEnabled).HasDatabaseDefault(true);

        // Potwierdzenie emaila działa bez logowania – wiersz szukany po SHA-256 tokenu. Częściowy: tylko oczekujące zmiany.
        builder.HasIndex(x => x.PendingEmailTokenHash)
            .IsUnique()
            .HasFilter("pending_email_token_hash IS NOT NULL")
            .HasDatabaseName("ux_user_settings_pending_email_token_hash");
    }
}
