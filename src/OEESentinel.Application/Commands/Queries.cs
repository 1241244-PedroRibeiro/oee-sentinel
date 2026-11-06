using OEESentinel.Domain.ValueObjects;

namespace OEESentinel.Application.Commands;

public sealed record GetMachineShiftQuery(Guid ShiftId);

public sealed record GetMachineOeeQuery(MachineCode MachineCode, DateTimeOffset From, DateTimeOffset To);

public sealed record GetDowntimeParetoQuery(MachineCode MachineCode, DateTimeOffset From, DateTimeOffset To);

public sealed record ListRecentIngestionsQuery(int Limit = 20);
public sealed record ListMachinesQuery();