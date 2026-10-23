namespace DevPulse.Domain.Entities;

/// <summary>Element listy top 10 otwartych PR w snapshocie (jsonb, zawsze czytany i zastępowany w całości).</summary>
public sealed class OpenPullRequest
{
    public OpenPullRequest(int number, string title, string url, string? author, DateTimeOffset createdAt, bool isDraft)
    {
        Number = number;
        Title = title;
        Url = url;
        Author = author;
        CreatedAt = createdAt;
        IsDraft = isDraft;
    }

    public int Number { get; private set; }

    public string Title { get; private set; }

    public string Url { get; private set; }

    /// <summary>Login autora; <c>null</c> dla usuniętego konta (ghost).</summary>
    public string? Author { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsDraft { get; private set; }
}
