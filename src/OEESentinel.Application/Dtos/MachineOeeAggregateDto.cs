namespace OEESentinel.Application.Dtos;

public sealed record MachineTotalsDto(
    double PlannedSeconds,
    double DowntimeSeconds,
    double OperatingSeconds,
    int TotalParts,
    int ApprovedParts,
    int RejectedParts);

public sealed record MachineOeeAggregateDto(
    string MachineCode,
    WindowDto Window,
    int ShiftsIncluded,
    MachineTotalsDto Totals,
    double Availability,
    double Performance,
    double Quality,
    double Oee,
    DateTimeOffset LastUpdatedAt);