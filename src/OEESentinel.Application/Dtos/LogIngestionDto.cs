using OEESentinel.Domain.Entities;

namespace OEESentinel.Application.Dtos;

public sealed record LogIngestionDto(
    Guid Id,
    string MachineCode,
    string OriginalFileName,
    string Status,
    int RowsProcessed,
    int RowsRejected,
    IReadOnlyList<RejectedRowDto> RejectedSample,
    DateTimeOffset UploadedAt,
    DateTimeOffset? CompletedAt,
    string? FailureReason)
{
    public static LogIngestionDto FromDomain(LogIngestion ing) => new(
        Id: ing.Id,
        MachineCode: ing.MachineCode.Value,
        OriginalFileName: ing.OriginalFileName,
        Status: ing.Status.ToString(),
        RowsProcessed: ing.RowsProcessed,
        RowsRejected: ing.RowsRejected,
        RejectedSample: ing.RejectedSample.Select(RejectedRowDto.FromDomain).ToList(),
        UploadedAt: ing.UploadedAt,
        CompletedAt: ing.CompletedAt,
        FailureReason: ing.FailureReason);
}