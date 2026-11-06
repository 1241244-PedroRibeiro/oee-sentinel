using Microsoft.EntityFrameworkCore;
using OEESentinel.Application.Ports.Outbound;
using OEESentinel.Domain.Entities;

namespace OEESentinel.Infrastructure.Persistence.Repositories;

public sealed class EfLogIngestionRepository : ILogIngestionRepository
{
    private readonly OEESentinelDbContext _ctx;

    public EfLogIngestionRepository(OEESentinelDbContext ctx) => _ctx = ctx;

    public async Task AddAsync(LogIngestion ingestion, CancellationToken ct)
    {
        await _ctx.LogIngestions.AddAsync(ingestion, ct);
    }

    public async Task PersistFailedAsync(LogIngestion ingestion, CancellationToken ct)
    {
        // The main transaction may have rolled back, so the entity's original
        // INSERT never landed. Detach and re-add so EF issues a fresh INSERT.
        var entry = _ctx.Entry(ingestion);
        if (entry.State != EntityState.Detached)
            entry.State = EntityState.Detached;

        _ctx.LogIngestions.Add(ingestion);
        await _ctx.SaveChangesAsync(ct);
    }

    public async Task<LogIngestion?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await _ctx.LogIngestions
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == id, ct);

    public async Task<IReadOnlyList<LogIngestion>> ListRecentAsync(int limit, CancellationToken ct) =>
        await _ctx.LogIngestions
            .AsNoTracking()
            .OrderByDescending(i => i.UploadedAt)
            .Take(limit)
            .ToListAsync(ct);
}