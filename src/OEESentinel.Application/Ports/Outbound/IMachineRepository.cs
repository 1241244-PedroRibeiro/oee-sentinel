using OEESentinel.Domain.Entities;
using OEESentinel.Domain.ValueObjects;

namespace OEESentinel.Application.Ports.Outbound;

public interface IMachineRepository
{
    Task<IReadOnlyList<Machine>> ListActiveAsync(CancellationToken ct);
    Task<Machine?> GetByCodeAsync(MachineCode code, CancellationToken ct);
}