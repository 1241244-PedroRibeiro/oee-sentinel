using OEESentinel.Domain.Records;

namespace OEESentinel.Application.Dtos;

public sealed record RejectedRowDto(int LineNumber, string RawLine, string Reason)
{
    public static RejectedRowDto FromDomain(RejectedRow row) =>
        new(row.LineNumber, row.RawLine, row.Reason);
}