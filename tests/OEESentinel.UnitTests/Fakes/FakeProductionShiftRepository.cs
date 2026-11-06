using OEESentinel.Application.Ports.Outbound;
using OEESentinel.Domain.Entities;
using OEESentinel.Domain.ValueObjects;

namespace OEESentinel.UnitTests.Fakes;

public sealed class FakeProductionShiftRepository : IProductionShiftRepository
{
    public List<ProductionShift> Added { get; } = new();
    public ProductionShift? ExistingShift { get; set; }

    public Task AddAsync(ProductionShift shift, CancellationToken ct)
    {
        Added.Add(shift);
        return Task.CompletedTask;
    }

    public Task<ProductionShift?> FindActiveByMachineAndWindowAsync(
        MachineCode code, TimeRange window, CancellationToken ct) =>
        Task.FromResult(ExistingShift);

    // Novos métodos Fase 5A:
    public Task<ProductionShift?> GetByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Added.FirstOrDefault(s => s.Id == id));

    public Task<IReadOnlyList<ProductionShift>> ListActiveByDateRangeAsync(
        MachineCode code, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ProductionShift>>(
            Added.Where(s => s.MachineCode == code).ToList());
}