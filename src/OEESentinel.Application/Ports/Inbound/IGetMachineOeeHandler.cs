using OEESentinel.Application.Commands;
using OEESentinel.Application.Dtos;

namespace OEESentinel.Application.Ports.Inbound;

public interface IGetMachineOeeHandler
{
    Task<MachineOeeAggregateDto> HandleAsync(GetMachineOeeQuery q, CancellationToken ct);
}