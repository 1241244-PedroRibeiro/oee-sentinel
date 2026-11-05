using Microsoft.EntityFrameworkCore;
using OEESentinel.Domain.Entities;
using OEESentinel.Infrastructure.Persistence.Entities;

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

    public DbSet<DowntimeReason> DowntimeReasons => Set<DowntimeReason>();
    public DbSet<LogIngestion> LogIngestions => Set<LogIngestion>();
    public DbSet<ProductionShift> ProductionShifts => Set<ProductionShift>();
    public DbSet<ProductionEventRow> ProductionEvents => Set<ProductionEventRow>();
}