using OEESentinel.Domain.Entities;

namespace OEESentinel.Application.Ports.Outbound;

public interface ILogIngestionRepository
{
    Task AddAsync(LogIngestion ingestion, CancellationToken ct);

    /// <summary>
    /// Re-inserts an ingestion after the main transaction has rolled back.
    /// Clears EF tracking before re-adding so the original in-memory instance survives.
    /// </summary>
    Task PersistFailedAsync(LogIngestion ingestion, CancellationToken ct);

    Task<LogIngestion?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<LogIngestion>> ListRecentAsync(int limit, CancellationToken ct);
}