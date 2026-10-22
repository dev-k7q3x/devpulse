using Microsoft.EntityFrameworkCore;

namespace DevPulse.Infrastructure.Persistence;

public sealed class DevPulseDbContext(DbContextOptions<DevPulseDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DevPulseDbContext).Assembly);
    }
}
