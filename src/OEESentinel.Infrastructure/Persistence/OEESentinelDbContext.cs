using Microsoft.EntityFrameworkCore;
using OEESentinel.Domain.Entities;

namespace OEESentinel.Infrastructure.Persistence;

public class OEESentinelDbContext : DbContext
{
    public OEESentinelDbContext(DbContextOptions<OEESentinelDbContext> options) : base(options) { }

    public DbSet<Machine> Machines => Set<Machine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OEESentinelDbContext).Assembly);
    }
}