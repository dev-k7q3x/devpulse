using DevPulse.Domain.Entities;
using DevPulse.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevPulse.Infrastructure.Persistence.Configurations;

internal sealed class TrackedRepositoryConfiguration : IEntityTypeConfiguration<TrackedRepository>
{
    public void Configure(EntityTypeBuilder<TrackedRepository> builder)
    {
        builder.ToTable("tracked_repositories");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        builder.Property(x => x.GitHubRepoId).HasColumnName("github_repo_id");
        builder.Property(x => x.GitHubNodeId).HasColumnName("github_node_id").HasMaxLength(TrackedRepository.GitHubNodeIdMaxLength);
        builder.Property(x => x.Owner).HasMaxLength(RepoFullName.OwnerMaxLength);
        builder.Property(x => x.Name).HasMaxLength(RepoFullName.NameMaxLength);
        builder.Property(x => x.FullName).HasMaxLength(TrackedRepository.FullNameMaxLength);
        builder.Property(x => x.HtmlUrl).HasMaxLength(TrackedRepository.HtmlUrlMaxLength);
        builder.Property(x => x.DefaultBranch).HasMaxLength(TrackedRepository.DefaultBranchMaxLength);
        builder.Property(x => x.CreatedAt).HasDatabaseDefaultSql("now()");

        // Brak duplikatów repo u użytkownika; user_id jako pierwsza kolumna obsługuje też „repo użytkownika X” i pokrywa FK.
        builder.HasIndex(x => new { x.UserId, x.GitHubRepoId })
            .IsUnique()
            .HasDatabaseName("ux_tracked_repositories_user_id_github_repo_id");
    }
}
