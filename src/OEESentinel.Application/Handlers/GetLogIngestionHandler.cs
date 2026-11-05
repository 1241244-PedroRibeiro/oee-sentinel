using OEESentinel.Application.Dtos;
using OEESentinel.Application.Ports.Inbound;
using OEESentinel.Application.Ports.Outbound;

namespace OEESentinel.Application.Handlers;

public sealed class GetLogIngestionHandler : IGetLogIngestionHandler
{
    private readonly ILogIngestionRepository _repo;

    public GetLogIngestionHandler(ILogIngestionRepository repo) => _repo = repo;

    public async Task<LogIngestionDto?> HandleAsync(Guid ingestionId, CancellationToken ct)
    {
        var ing = await _repo.GetByIdAsync(ingestionId, ct);
        return ing is null ? null : LogIngestionDto.FromDomain(ing);
    }
}