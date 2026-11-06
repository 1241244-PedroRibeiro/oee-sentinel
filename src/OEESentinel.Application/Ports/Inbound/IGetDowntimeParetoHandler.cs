using OEESentinel.Application.Commands;
using OEESentinel.Application.Dtos;

namespace OEESentinel.Application.Ports.Inbound;

public interface IGetDowntimeParetoHandler
{
    Task<DowntimeParetoDto> HandleAsync(GetDowntimeParetoQuery q, CancellationToken ct);
}