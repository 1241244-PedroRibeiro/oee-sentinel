using EFCore.BulkExtensions;
using OEESentinel.Application.Ports.Outbound;
using OEESentinel.Domain.Records;
using OEESentinel.Infrastructure.Persistence.Entities;

namespace OEESentinel.Infrastructure.Persistence.Repositories;

public sealed class EfProductionEventRepository : IProductionEventRepository
{
    private readonly OEESentinelDbContext _ctx;

    public EfProductionEventRepository(OEESentinelDbContext ctx) => _ctx = ctx;

    public async Task BulkInsertAsync(
        Guid ingestionId,
        IReadOnlyList<ProductionEvent> events,
        CancellationToken ct)
    {
        if (events.Count == 0) return;

        var rows = events.Select(e => new ProductionEventRow
        {
            IngestionId = ingestionId,
            RecordedAt = e.Timestamp,
            Quantity = e.Quantity,
            ErrorCode = e.ErrorCode,
            SourceLineNumber = e.SourceLineNumber
        }).ToList();

        await _ctx.BulkInsertAsync(rows, new BulkConfig
        {
            PreserveInsertOrder = false,
            SetOutputIdentity = false
        }, cancellationToken: ct);
    }
}