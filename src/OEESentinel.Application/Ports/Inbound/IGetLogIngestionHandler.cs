using OEESentinel.Application.Dtos;

namespace OEESentinel.Application.Ports.Inbound;

public interface IGetLogIngestionHandler
{
    Task<LogIngestionDto?> HandleAsync(Guid ingestionId, CancellationToken ct);
}