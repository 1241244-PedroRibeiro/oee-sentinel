using Microsoft.EntityFrameworkCore;
using OEESentinel.Domain.Entities;
using OEESentinel.Domain.ValueObjects;

namespace OEESentinel.Infrastructure.Persistence.Seeding;

public static class BaselineSeed
{
    public static async Task SeedAsync(OEESentinelDbContext context)
    {
        if (await context.Machines.AnyAsync()) return; // Idempotente

        var machines = new List<Machine>
        {
            new(new MachineCode("MAQ-01"), "Injection Press A1", new IdealCycleTime(2.5), Duration.FromHours(8)),
            new(new MachineCode("MAQ-02"), "Injection Press A2", new IdealCycleTime(2.8), Duration.FromHours(8)),
            new(new MachineCode("PRESSA-B2"), "Assembly Press B2", new IdealCycleTime(4.0), Duration.FromHours(8))
        };

        context.Machines.AddRange(machines);
        await context.SaveChangesAsync();
    }
}