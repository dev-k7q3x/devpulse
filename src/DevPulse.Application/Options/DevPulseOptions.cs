namespace DevPulse.Application.Options;

/// <summary>Sekcja <c>Repositories</c>. Limit utrzymuje dashboard poniżej 2 s bez paginacji i przewidywalny koszt GraphQL.</summary>
public sealed class RepositoriesOptions
{
    public const string SectionName = "Repositories";

    public int MaxPerUser { get; set; } = 50;
}

/// <summary>Sekcja <c>Snapshot</c> – progi świeżości cache repo.</summary>
public sealed class SnapshotOptions
{
    public const string SectionName = "Snapshot";

    /// <summary>Interwał SnapshotRefreshJob i próg odświeżenia przed digestem.</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Starszy snapshot to karta <c>Stale</c> na dashboardzie.</summary>
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Aktywny użytkownik: logował się w tym oknie albo ma włączony digest.</summary>
    public TimeSpan ActiveUserWindow { get; set; } = TimeSpan.FromDays(14);
}

/// <summary>Sekcja <c>Admin</c> – loginy GitHub z dostępem do /hangfire.</summary>
public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    public IReadOnlyList<string> Logins { get; set; } = [];
}
