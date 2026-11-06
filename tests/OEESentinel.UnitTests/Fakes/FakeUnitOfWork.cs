using OEESentinel.Application.Ports.Outbound;

namespace OEESentinel.UnitTests.Fakes;

public sealed class FakeUnitOfWork : IUnitOfWork
{
    public bool Began { get; private set; }
    public bool Committed { get; private set; }
    public bool RolledBack { get; private set; }
    public int SaveCount { get; private set; }

    public Task BeginTransactionAsync(CancellationToken ct)
    {
        Began = true;
        return Task.CompletedTask;
    }

    public Task CommitAsync(CancellationToken ct)
    {
        Committed = true;
        return Task.CompletedTask;
    }

    public Task RollbackAsync(CancellationToken ct)
    {
        RolledBack = true;
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}