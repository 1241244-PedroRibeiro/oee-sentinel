using OEESentinel.Application.Ports.Outbound;
using OEESentinel.Domain.Entities;
using OEESentinel.Domain.ValueObjects;

namespace OEESentinel.UnitTests.Fakes;

public sealed class FakeMachineRepository : IMachineRepository
{
    private readonly Dictionary<string, Machine> _machines = new();

    public void Add(Machine machine) => _machines[machine.Code.Value] = machine;

    public Task<Machine?> GetByCodeAsync(MachineCode code, CancellationToken ct) =>
        Task.FromResult(_machines.GetValueOrDefault(code.Value));

    public Task<IReadOnlyList<Machine>> ListActiveAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Machine>>(
            _machines.Values.Where(m => m.IsActive).ToList());
}