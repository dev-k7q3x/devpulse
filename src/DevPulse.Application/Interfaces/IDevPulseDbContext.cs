using DevPulse.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevPulse.Application.Interfaces;

/// <summary>
/// Dostęp do danych dla serwisów Application. Bez global query filters – każde zapytanie o dane użytkownika
/// filtruje jawnie po <c>userId</c>. Scoped: singletony używają <c>IDbContextFactory</c>.
/// </summary>
public interface IDevPulseDbContext
{
    DbSet<User> Users { get; }

    DbSet<UserSettings> UserSettings { get; }

    DbSet<TrackedRepository> TrackedRepositories { get; }

    DbSet<RepositorySnapshot> RepositorySnapshots { get; }

    DbSet<Digest> Digests { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
