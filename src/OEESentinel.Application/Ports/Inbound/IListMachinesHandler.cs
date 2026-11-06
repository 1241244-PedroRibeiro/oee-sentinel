using OEESentinel.Application.Commands;
using OEESentinel.Application.Dtos;

namespace OEESentinel.Application.Ports.Inbound;

public interface IListMachinesHandler
{
    Task<IReadOnlyList<MachineDto>> HandleAsync(ListMachinesQuery query, CancellationToken ct);
}