using OEESentinel.Application.Commands;
using OEESentinel.Application.Dtos;
using OEESentinel.Application.Ports.Inbound;
using OEESentinel.Application.Ports.Outbound;

namespace OEESentinel.Application.Handlers;

public class ListMachinesHandler : IListMachinesHandler
{
    private readonly IMachineRepository _repository;

    public ListMachinesHandler(IMachineRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<MachineDto>> HandleAsync(ListMachinesQuery query, CancellationToken ct = default)
    {
        var machines = await _repository.ListActiveAsync(ct);

        return machines.Select(m => new MachineDto(
            m.Id,
            m.Code.Value,
            m.Name,
            m.IdealCycleTime.Seconds,
            m.PlannedShiftDuration.Seconds,
            m.IsActive
        )).ToList();
    }
}