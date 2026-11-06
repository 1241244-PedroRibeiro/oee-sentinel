using OEESentinel.Application.Ports.Outbound;
using OEESentinel.Domain.Records;

namespace OEESentinel.UnitTests.Fakes;

public sealed class FakeProductionEventRepository : IProductionEventRepository
{
    public List<ProductionEvent> AllEvents { get; } = new();
    public int BulkInsertCalls { get; private set; }

    public Task BulkInsertAsync(
        Guid ingestionId, IReadOnlyList<ProductionEvent> events, CancellationToken ct)
    {
        BulkInsertCalls++;
        AllEvents.AddRange(events);
        return Task.CompletedTask;
    }
}