using Microsoft.EntityFrameworkCore;
using OEESentinel.Application.Ports.Outbound;
using OEESentinel.Domain.Entities;
using OEESentinel.Domain.ValueObjects;

namespace OEESentinel.Infrastructure.Persistence.Repositories;

public sealed class EfProductionShiftRepository : IProductionShiftRepository
{
    private readonly OEESentinelDbContext _ctx;

    public EfProductionShiftRepository(OEESentinelDbContext ctx) => _ctx = ctx;

    public async Task AddAsync(ProductionShift shift, CancellationToken ct)
    {
        await _ctx.ProductionShifts.AddAsync(shift, ct);
    }

    public async Task<ProductionShift?> FindActiveByMachineAndWindowAsync(
        MachineCode machineCode, TimeRange window, CancellationToken ct) =>
        await _ctx.ProductionShifts
            .AsNoTracking()
            .FirstOrDefaultAsync(s =>
                s.MachineCode == machineCode &&
                s.Window.Start == window.Start &&
                s.Window.End == window.End &&
                s.SupersededBy == null, ct);

    public async Task<ProductionShift?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await _ctx.ProductionShifts.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<IReadOnlyList<ProductionShift>> ListActiveByDateRangeAsync(
        MachineCode machineCode, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        await _ctx.ProductionShifts
            .AsNoTracking()
            .Where(s => s.MachineCode == machineCode
                     && s.Window.Start >= from
                     && s.Window.End <= to
                     && s.SupersededBy == null)
            .OrderBy(s => s.Window.Start)
            .ToListAsync(ct);
}