using OEESentinel.Domain.ValueObjects;

namespace OEESentinel.Application.Commands;

public sealed record ProcessLogFileCommand(
    MachineCode MachineCode,
    TimeRange Window,
    string OriginalFileName,
    Stream FileStream,
    DateTimeOffset RecordedAt);