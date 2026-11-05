namespace OEESentinel.Application.Dtos;

public record MachineDto(
    Guid Id,
    string Code,
    string Name,
    double IdealCycleSeconds,
    double PlannedShiftSeconds,
    bool IsActive);