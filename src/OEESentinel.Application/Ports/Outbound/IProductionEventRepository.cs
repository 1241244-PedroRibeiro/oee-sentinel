using OEESentinel.Domain.Records;

namespace OEESentinel.Application.Ports.Outbound;

public interface IProductionEventRepository
{
    Task BulkInsertAsync(
        Guid ingestionId,
        IReadOnlyList<ProductionEvent> events,
        CancellationToken ct);
}