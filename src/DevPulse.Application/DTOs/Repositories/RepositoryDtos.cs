using System.Text.Json.Serialization;

namespace DevPulse.Application.DTOs.Repositories;

/// <summary>Query <c>GET /api/repositories/available</c> – paginacja i wyszukiwanie w pamięci po pobraniu pełnej listy.</summary>
public sealed record AvailableRepositoriesRequest
{
    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 50;

    public string? Search { get; init; }

    /// <summary>Pomija cache – osobny, ostrzejszy limit (<c>github-refresh</c>).</summary>
    public bool Refresh { get; init; }
}

public sealed record AvailableRepositoriesResponse(
    IReadOnlyList<AvailableRepositoryItem> Items,
    int Page,
    int PageSize,
    int TotalCount,
    DateTimeOffset FetchedAt);

public sealed record AvailableRepositoryItem(
    [property: JsonPropertyName("githubRepoId")] long GitHubRepoId,
    string FullName,
    string HtmlUrl,
    bool IsPrivate,
    string? Description,
    DateTimeOffset? PushedAt,
    bool IsTracked);

public sealed record TrackedRepositoryResponse(
    Guid Id,
    [property: JsonPropertyName("githubRepoId")] long GitHubRepoId,
    string FullName,
    string HtmlUrl,
    bool IsPrivate,
    DateTimeOffset AddedAt);

/// <summary>Samo id – metadane serwer bierze z GitHuba, więc klient nie podsunie fałszywej nazwy ani cudzego repo.</summary>
public sealed record RepositoryAddRequest
{
    [JsonPropertyName("githubRepoId")]
    public long GitHubRepoId { get; init; }
}
