using DevPulse.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevPulse.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.GitHubId).HasColumnName("github_id");
        builder.Property(x => x.Login).HasMaxLength(User.LoginMaxLength);
        builder.Property(x => x.AvatarUrl).HasMaxLength(User.AvatarUrlMaxLength);
        builder.Property(x => x.AccessTokenEncrypted).HasColumnType("text");
        builder.Property(x => x.TokenScopes).HasMaxLength(User.TokenScopesMaxLength);
        builder.Property(x => x.TokenInvalid).HasDatabaseDefault(false);
        builder.Property(x => x.CreatedAt).HasDatabaseDefaultSql("now()");

        // Upsert przy każdym logowaniu szuka po github_id; gwarantuje jedno konto DevPulse na konto GitHub.
        builder.HasIndex(x => x.GitHubId).IsUnique().HasDatabaseName("ux_users_github_id");
    }
}
