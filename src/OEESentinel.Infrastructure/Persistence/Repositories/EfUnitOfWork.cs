using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OEESentinel.Application.Ports.Outbound;

namespace OEESentinel.Infrastructure.Persistence.Repositories;

public sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly OEESentinelDbContext _ctx;
    private IDbContextTransaction? _tx;

    public EfUnitOfWork(OEESentinelDbContext ctx) => _ctx = ctx;

    public async Task BeginTransactionAsync(CancellationToken ct)
    {
        if (_tx != null)
            throw new InvalidOperationException("Transaction already active.");
        _tx = await _ctx.Database.BeginTransactionAsync(ct);
    }

    public async Task CommitAsync(CancellationToken ct)
    {
        if (_tx == null)
            throw new InvalidOperationException("No active transaction.");
        try
        {
            await _ctx.SaveChangesAsync(ct);
            await _tx.CommitAsync(ct);
        }
        finally
        {
            await _tx.DisposeAsync();
            _tx = null;
        }
    }

    public async Task RollbackAsync(CancellationToken ct)
    {
        if (_tx == null) { _ctx.ChangeTracker.Clear(); return; }
        try
        {
            await _tx.RollbackAsync(ct);
        }
        finally
        {
            await _tx.DisposeAsync();
            _tx = null;
            _ctx.ChangeTracker.Clear();
        }
    }

    public Task SaveChangesAsync(CancellationToken ct) => _ctx.SaveChangesAsync(ct);
}