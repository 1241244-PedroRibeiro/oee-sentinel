using OEESentinel.Application.Commands;
using OEESentinel.Application.Dtos;

namespace OEESentinel.Application.Ports.Inbound;

public interface IListRecentIngestionsHandler
{
    Task<IReadOnlyList<LogIngestionDto>> HandleAsync(ListRecentIngestionsQuery q, CancellationToken ct);
}