namespace OEESentinel.Application.Ports.Outbound;

public interface IUnitOfWork
{
    Task BeginTransactionAsync(CancellationToken ct);
    Task CommitAsync(CancellationToken ct);
    Task RollbackAsync(CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}