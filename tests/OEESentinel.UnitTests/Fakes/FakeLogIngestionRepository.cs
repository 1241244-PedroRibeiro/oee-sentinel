using OEESentinel.Application.Ports.Outbound;
using OEESentinel.Domain.Entities;

namespace OEESentinel.UnitTests.Fakes;

public sealed class FakeLogIngestionRepository : ILogIngestionRepository
{
    public List<LogIngestion> Added { get; } = new();
    public List<LogIngestion> Persisted { get; } = new();
    public LogIngestion? LastPersistedFailure { get; private set; }

    public Task AddAsync(LogIngestion ingestion, CancellationToken ct)
    {
        Added.Add(ingestion);
        return Task.CompletedTask;
    }

    public Task PersistFailedAsync(LogIngestion ingestion, CancellationToken ct)
    {
        Persisted.Add(ingestion);
        LastPersistedFailure = ingestion;
        return Task.CompletedTask;
    }

    public Task<LogIngestion?> GetByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult<LogIngestion?>(Added.Concat(Persisted).FirstOrDefault(i => i.Id == id));

    public Task<IReadOnlyList<LogIngestion>> ListRecentAsync(int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LogIngestion>>(
            Added.Concat(Persisted).OrderByDescending(i => i.UploadedAt).Take(limit).ToList());
}