using OEESentinel.Domain.ValueObjects;

namespace OEESentinel.Domain.Records;

public sealed record ProductionEvent
{
    public DateTimeOffset Timestamp { get; }
    public MachineCode MachineCode { get; }
    public int Quantity { get; }
    public string? ErrorCode { get; }
    public int SourceLineNumber { get; }
    public string RawLine { get; } // <-- NOVO

    public ProductionEvent(
        DateTimeOffset timestamp,
        MachineCode machineCode,
        int quantity,
        string? errorCode,
        int sourceLineNumber,
        string rawLine = "")
    {
        if (quantity < 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be non-negative.");
        if (sourceLineNumber <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceLineNumber), "SourceLineNumber must be positive.");

        Timestamp = timestamp;
        MachineCode = machineCode ?? throw new ArgumentNullException(nameof(machineCode));
        Quantity = quantity;
        ErrorCode = string.IsNullOrWhiteSpace(errorCode) ? null : errorCode;
        SourceLineNumber = sourceLineNumber;
        RawLine = rawLine;
    }
}