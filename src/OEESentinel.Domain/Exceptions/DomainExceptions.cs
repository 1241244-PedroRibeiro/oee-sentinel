using OEESentinel.Domain.ValueObjects;

namespace OEESentinel.Domain.Exceptions;

public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}

public sealed class MachineNotFoundDomainException : DomainException
{
    public string MachineCode { get; }

    public MachineNotFoundDomainException(string machineCode)
        : base($"Machine '{machineCode}' does not exist or has been deactivated.")
    {
        MachineCode = machineCode;
    }
}

public sealed class InvalidWindowDomainException : DomainException
{
    public InvalidWindowDomainException(string message) : base(message) { }
}

public sealed class DuplicateShiftDomainException : DomainException
{
    public Guid ExistingShiftId { get; }

    public DuplicateShiftDomainException(Guid existingShiftId)
        : base($"A current production shift already exists for the requested window (id: {existingShiftId}).")
    {
        ExistingShiftId = existingShiftId;
    }
}

public sealed class PlannedTimeZeroDomainException : DomainException
{
    public PlannedTimeZeroDomainException()
        : base("Machine.PlannedShiftDuration must be greater than zero.") { }
}

public sealed class DowntimeOverflowDomainException : DomainException
{
    public DowntimeOverflowDomainException(Duration planned, Duration totalDowntime)
        : base($"Total downtime ({totalDowntime.Seconds}s) exceeds planned production time ({planned.Seconds}s).") { }
}

public sealed class LogIngestionNotFoundDomainException : DomainException
{
    public Guid IngestionId { get; }

    public LogIngestionNotFoundDomainException(Guid ingestionId)
        : base($"Log ingestion '{ingestionId}' does not exist.")
    {
        IngestionId = ingestionId;
    }
}