using DevPulse.Domain.Entities;
using DevPulse.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevPulse.Infrastructure.Persistence.Configurations;

internal sealed class RepositorySnapshotConfiguration : IEntityTypeConfiguration<RepositorySnapshot>
{
    public void Configure(EntityTypeBuilder<RepositorySnapshot> builder)
    {
        builder.ToTable("repository_snapshots", table =>
        {
            table.HasCheckConstraint("ck_repository_snapshots_open_pr_count", "open_pr_count >= 0");
            table.HasCheckConstraint("ck_repository_snapshots_open_issue_count", "open_issue_count >= 0");
            table.HasCheckConstraint("ck_repository_snapshots_ci_status", ConfigurationExtensions.EnumCheckSql<CiStatus>("ci_status"));

            // Kolekcja owned (jsonb) nie może być w EF oznaczona jako wymagana – NOT NULL wymuszamy CHECK-iem.
            table.HasCheckConstraint("ck_repository_snapshots_open_pull_requests", "open_pull_requests IS NOT NULL");
        });

        builder.HasKey(x => x.TrackedRepositoryId);
        builder.HasOne<TrackedRepository>()
            .WithOne()
            .HasForeignKey<RepositorySnapshot>(x => x.TrackedRepositoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(x => x.CiStatus).HasEnumAsText();
        builder.Property(x => x.CiCommitSha).HasMaxLength(RepositorySnapshot.CommitShaLength).IsFixedLength();
        builder.Property(x => x.LastError).HasMaxLength(RepositorySnapshot.LastErrorMaxLength);

        // Lista top 10 PR jest zawsze czytana i zastępowana w całości – jsonb zamiast tabeli podrzędnej.
        builder.OwnsMany(x => x.OpenPullRequests, pullRequests => pullRequests.ToJson("open_pull_requests"));
    }
}
