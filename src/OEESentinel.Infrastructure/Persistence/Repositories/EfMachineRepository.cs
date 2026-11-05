using Microsoft.EntityFrameworkCore;
using OEESentinel.Application.Ports.Outbound;
using OEESentinel.Domain.Entities;
using OEESentinel.Domain.ValueObjects;

namespace OEESentinel.Infrastructure.Persistence.Repositories;

public class EfMachineRepository : IMachineRepository
{
    private readonly OEESentinelDbContext _context;

    public EfMachineRepository(OEESentinelDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<Machine>> ListActiveAsync(CancellationToken ct)
    {
        return await _context.Machines
            .AsNoTracking()
            .Where(m => m.IsActive)
            .OrderBy(m => m.Code)
            .ToListAsync(ct);
    }

    public async Task<Machine?> GetByCodeAsync(MachineCode code, CancellationToken ct)
    {
        return await _context.Machines
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Code == code, ct);
    }
}