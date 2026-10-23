using DevPulse.Application.Interfaces;
using DevPulse.Domain.Entities;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DevPulse.Infrastructure.Persistence;

public sealed class DevPulseDbContext(DbContextOptions<DevPulseDbContext> options)
    : DbContext(options), IDevPulseDbContext, IDataProtectionKeyContext
{
    public DbSet<User> Users => Set<User>();

    public DbSet<UserSettings> UserSettings => Set<UserSettings>();

    public DbSet<TrackedRepository> TrackedRepositories => Set<TrackedRepository>();

    public DbSet<RepositorySnapshot> RepositorySnapshots => Set<RepositorySnapshot>();

    public DbSet<Digest> Digests => Set<Digest>();

    /// <summary>Pierścień kluczy Data Protection – kontenery Railway są efemeryczne, więc klucze muszą być w bazie.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DevPulseDbContext).Assembly);
    }
}
