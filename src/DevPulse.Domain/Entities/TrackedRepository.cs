using DevPulse.Domain.ValueObjects;

namespace DevPulse.Domain.Entities;

/// <summary>Repozytorium wybrane przez użytkownika. To samo repo u dwóch użytkowników to dwa wiersze (brak współdzielonego cache).</summary>
public sealed class TrackedRepository
{
    public const int GitHubNodeIdMaxLength = 64;
    public const int FullNameMaxLength = RepoFullName.OwnerMaxLength + 1 + RepoFullName.NameMaxLength;
    public const int HtmlUrlMaxLength = 512;
    public const int DefaultBranchMaxLength = 255;

    private TrackedRepository()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>Stabilne id repo (<c>databaseId</c> z GraphQL).</summary>
    public long GitHubRepoId { get; private set; }

    /// <summary>Globalne id GraphQL – snapshoty pobieramy przez <c>nodes(ids:)</c>, odporne na zmianę nazwy.</summary>
    public string GitHubNodeId { get; private set; } = string.Empty;

    public string Owner { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string FullName { get; private set; } = string.Empty;

    public string HtmlUrl { get; private set; } = string.Empty;

    public string DefaultBranch { get; private set; } = string.Empty;

    public bool IsPrivate { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static TrackedRepository Create(
        Guid userId,
        long gitHubRepoId,
        string gitHubNodeId,
        RepoFullName fullName,
        string htmlUrl,
        string defaultBranch,
        bool isPrivate,
        DateTimeOffset now)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("Brak id użytkownika.", nameof(userId));
        }

        if (gitHubRepoId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gitHubRepoId), gitHubRepoId, "Id repozytorium musi być dodatnie.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(gitHubNodeId);
        if (gitHubNodeId.Length > GitHubNodeIdMaxLength)
        {
            throw new ArgumentException($"Node id ma maksymalnie {GitHubNodeIdMaxLength} znaków.", nameof(gitHubNodeId));
        }

        var repository = new TrackedRepository
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            GitHubRepoId = gitHubRepoId,
            GitHubNodeId = gitHubNodeId,
            CreatedAt = Guard.Utc(now, nameof(now)),
        };
        repository.UpdateMetadata(fullName, htmlUrl, defaultBranch, isPrivate, now);
        return repository;
    }

    /// <summary>Repo może zmienić nazwę, właściciela, default branch i widoczność – aktualizowane przy odświeżeniu.</summary>
    public void UpdateMetadata(RepoFullName fullName, string htmlUrl, string defaultBranch, bool isPrivate, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(fullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(htmlUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultBranch);

        if (htmlUrl.Length > HtmlUrlMaxLength)
        {
            throw new ArgumentException($"URL ma maksymalnie {HtmlUrlMaxLength} znaków.", nameof(htmlUrl));
        }

        if (defaultBranch.Length > DefaultBranchMaxLength)
        {
            throw new ArgumentException($"Nazwa brancha ma maksymalnie {DefaultBranchMaxLength} znaków.", nameof(defaultBranch));
        }

        Owner = fullName.Owner;
        Name = fullName.Name;
        FullName = fullName.Value;
        HtmlUrl = htmlUrl;
        DefaultBranch = defaultBranch;
        IsPrivate = isPrivate;
        UpdatedAt = Guard.Utc(now, nameof(now));
    }
}
