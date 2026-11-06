namespace OEESentinel.Domain.Records;

public sealed record RejectedRow(int LineNumber, string RawLine, string Reason);