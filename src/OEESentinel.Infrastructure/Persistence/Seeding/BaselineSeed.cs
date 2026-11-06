using Microsoft.EntityFrameworkCore;
using OEESentinel.Domain.Entities;
using OEESentinel.Domain.Enums;
using OEESentinel.Domain.ValueObjects;

namespace OEESentinel.Infrastructure.Persistence.Seeding;

public static class BaselineSeed
{
    public static async Task SeedAsync(
        OEESentinelDbContext context,
        CancellationToken ct = default)
    {
        // ── Machines ────────────────────────────────────────────────────────
        if (!await context.Machines.AnyAsync(ct))
        {
            var machines = new List<Machine>
            {
                new(new MachineCode("MAQ-01"),    "Injection Press A1", new IdealCycleTime(2.5), Duration.FromHours(8)),
                new(new MachineCode("MAQ-02"),    "Injection Press A2", new IdealCycleTime(2.8), Duration.FromHours(8)),
                new(new MachineCode("PRESSA-B2"), "Assembly Press B2",  new IdealCycleTime(4.0), Duration.FromHours(8))
            };

            context.Machines.AddRange(machines);
            await context.SaveChangesAsync(ct);
        }

        // ── Downtime reasons ────────────────────────────────────────────────
        if (!await context.DowntimeReasons.AnyAsync(ct))
        {
            var reasons = new[]
            {
                DowntimeReason.Create(new DowntimeReasonCode("PLANNED_BREAK"),         "Scheduled shift break",             DowntimeCategory.PLANNED),
                DowntimeReason.Create(new DowntimeReasonCode("PLANNED_MAINTENANCE"),   "Scheduled preventive maintenance",  DowntimeCategory.PLANNED),
                DowntimeReason.Create(new DowntimeReasonCode("UNPLANNED_MECHANICAL"),  "Mechanical failure",                DowntimeCategory.UNPLANNED),
                DowntimeReason.Create(new DowntimeReasonCode("UNPLANNED_TOOL_CHANGE"), "Unplanned tool change",             DowntimeCategory.UNPLANNED),
                DowntimeReason.Create(new DowntimeReasonCode("UNPLANNED_ELECTRICAL"),  "Electrical failure",                DowntimeCategory.UNPLANNED),
                DowntimeReason.Create(new DowntimeReasonCode("UNPLANNED_OPERATOR"),    "Operator absence or error",         DowntimeCategory.UNPLANNED)
            };

            context.DowntimeReasons.AddRange(reasons);
            await context.SaveChangesAsync(ct);
        }
    }
}