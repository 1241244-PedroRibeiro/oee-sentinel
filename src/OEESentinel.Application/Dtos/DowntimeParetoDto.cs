namespace OEESentinel.Application.Dtos;

public sealed record ParetoEntryDto(
    string ReasonCode,
    string Category,
    string Description,
    double Seconds,
    double Percentage);

public sealed record DowntimeParetoDto(
    string MachineCode,
    WindowDto Window,
    int TotalShifts,
    double TotalDowntimeSeconds,
    IReadOnlyList<ParetoEntryDto> Entries);