using OEESentinel.Domain.Entities;

namespace OEESentinel.Application.Dtos;

public sealed record LogUploadResponse(
    Guid IngestionId,
    string Status,
    string MachineCode,
    WindowDto Window,
    int RowsProcessed,
    int RowsRejected,
    IReadOnlyList<RejectedRowDto> RejectedSample,
    Guid ProductionShiftId,
    DateTimeOffset CompletedAt)
{
    public static LogUploadResponse FromDomain(LogIngestion ingestion, ProductionShift shift) => new(
        IngestionId: ingestion.Id,
        Status: ingestion.Status.ToString(),
        MachineCode: ingestion.MachineCode.Value,
        Window: new WindowDto(shift.Window.Start, shift.Window.End),
        RowsProcessed: ingestion.RowsProcessed,
        RowsRejected: ingestion.RowsRejected,
        RejectedSample: ingestion.RejectedSample.Select(RejectedRowDto.FromDomain).ToList(),
        ProductionShiftId: shift.Id,
        CompletedAt: ingestion.CompletedAt
            ?? throw new InvalidOperationException("Cannot build response from a non-completed ingestion."));
}