using OEESentinel.Application.Commands;
using OEESentinel.Application.Dtos;
using OEESentinel.Application.Ports.Inbound;
using OEESentinel.Application.Ports.Outbound;

namespace OEESentinel.Application.Handlers;

public sealed class ListRecentIngestionsHandler : IListRecentIngestionsHandler
{
    private readonly ILogIngestionRepository _repo;

    public ListRecentIngestionsHandler(ILogIngestionRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<LogIngestionDto>> HandleAsync(ListRecentIngestionsQuery q, CancellationToken ct)
    {
        var list = await _repo.ListRecentAsync(q.Limit, ct);
        return list.Select(LogIngestionDto.FromDomain).ToList();
    }
}