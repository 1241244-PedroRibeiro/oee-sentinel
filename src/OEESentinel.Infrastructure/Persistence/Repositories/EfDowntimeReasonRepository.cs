using Microsoft.EntityFrameworkCore;
using OEESentinel.Application.Ports.Outbound;
using OEESentinel.Domain.Entities;
using OEESentinel.Domain.ValueObjects;

namespace OEESentinel.Infrastructure.Persistence.Repositories;

public sealed class EfDowntimeReasonRepository : IDowntimeReasonRepository
{
    private readonly OEESentinelDbContext _ctx;

    public EfDowntimeReasonRepository(OEESentinelDbContext ctx) => _ctx = ctx;

    public async Task<IReadOnlyList<DowntimeReason>> ListActiveAsync(CancellationToken ct) =>
        await _ctx.DowntimeReasons
            .AsNoTracking()
            .Where(r => r.IsActive)
            .ToListAsync(ct);

    public async Task<DowntimeReason?> GetByCodeAsync(DowntimeReasonCode code, CancellationToken ct) =>
        await _ctx.DowntimeReasons
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Code == code, ct);
}