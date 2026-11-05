using OEESentinel.Domain.ValueObjects;

namespace OEESentinel.Domain.Entities;

public class Machine
{
    public Guid Id { get; private set; }
    public MachineCode Code { get; private set; } = null!;
    public string Name { get; private set; } = string.Empty;
    public IdealCycleTime IdealCycleTime { get; private set; } = null!;
    public Duration PlannedShiftDuration { get; private set; } = null!;
    public bool IsActive { get; private set; }

    private Machine() { } // Para o EF Core

    public Machine(MachineCode code, string name, IdealCycleTime idealCycleTime, Duration plannedShiftDuration)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));

        Id = Guid.NewGuid();
        Code = code;
        Name = name.Trim();
        IdealCycleTime = idealCycleTime;
        PlannedShiftDuration = plannedShiftDuration;
        IsActive = true;
    }

    // Soft Deactivation
    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}