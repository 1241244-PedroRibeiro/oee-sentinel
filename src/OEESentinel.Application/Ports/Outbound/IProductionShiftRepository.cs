using OEESentinel.Domain.Entities;
using OEESentinel.Domain.ValueObjects;

namespace OEESentinel.Application.Ports.Outbound;

public interface IProductionShiftRepository
{
    Task AddAsync(ProductionShift shift, CancellationToken ct);

    Task<ProductionShift?> FindActiveByMachineAndWindowAsync(
        MachineCode machineCode,
        TimeRange window,
        CancellationToken ct);

    Task<ProductionShift?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<ProductionShift>> ListActiveByDateRangeAsync(
        MachineCode machineCode, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}