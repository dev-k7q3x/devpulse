using DevPulse.Domain.Entities;
using DevPulse.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevPulse.Infrastructure.Persistence.Configurations;

internal sealed class DigestConfiguration : IEntityTypeConfiguration<Digest>
{
    public void Configure(EntityTypeBuilder<Digest> builder)
    {
        builder.ToTable("digests", table =>
        {
            table.HasCheckConstraint("ck_digests_status", ConfigurationExtensions.EnumCheckSql<DigestStatus>("status"));
            table.HasCheckConstraint("ck_digests_sent_at", "(status = 'Sent') = (sent_at IS NOT NULL)");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        builder.Property(x => x.TimeZoneId).HasMaxLength(Digest.TimeZoneIdMaxLength);
        builder.Property(x => x.Status).HasEnumAsText();
        builder.Property(x => x.Subject).HasMaxLength(Digest.SubjectMaxLength);
        builder.Property(x => x.RecipientEmail).HasMaxLength(Digest.RecipientEmailMaxLength);
        builder.Property(x => x.HtmlBody).HasColumnType("text");
        builder.Property(x => x.AiUsed).HasDatabaseDefault(false);
        builder.Property(x => x.AiModel).HasMaxLength(Domain.ValueObjects.DigestAiUsage.ModelMaxLength);
        builder.Property(x => x.EmailProviderMessageId).HasMaxLength(Digest.EmailProviderMessageIdMaxLength);
        builder.Property(x => x.AttemptCount).HasDatabaseDefault((short)0);
        builder.Property(x => x.Error).HasMaxLength(Digest.ErrorMaxLength);
        builder.Property(x => x.CreatedAt).HasDatabaseDefaultSql("now()");

        // Dane niezmienne po zapisie, zawsze czytane razem z digestem – jsonb zamiast tabel podrzędnych.
        builder.OwnsMany(x => x.Priorities, priorities =>
        {
            priorities.ToJson("priorities");
            priorities.Property(p => p.Severity).HasConversion<string>();
        });
        builder.OwnsMany(x => x.RepositorySummary, summary =>
        {
            summary.ToJson("repository_summary");
            summary.Property(s => s.CiStatus).HasConversion<string>();
        });

        // Idempotencja digestu (insert Pending, unique violation = koniec bez wysyłki) i lista historii użytkownika.
        builder.HasIndex(x => new { x.UserId, x.DigestDate })
            .IsUnique()
            .HasDatabaseName("ux_digests_user_id_digest_date");

        // RetentionJob: DELETE … WHERE created_at < now() - 30 dni dla wszystkich użytkowników.
        builder.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_digests_created_at");
    }
}
