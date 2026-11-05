using OEESentinel.Domain.Entities;

namespace OEESentinel.Application.Dtos;

public sealed record DowntimeEntryDto(string ReasonCode, string Category, double Seconds);

public sealed record ProductionShiftSummaryDto(
    Guid Id,
    string MachineCode,
    WindowDto Window,
    int TotalParts,
    int ApprovedParts,
    int RejectedParts,
    IReadOnlyList<DowntimeEntryDto> DowntimeBreakdown,
    double Availability,
    double Performance,
    double Quality,
    double Oee,
    Guid SourceIngestionId,
    Guid? SupersededBy,
    DateTimeOffset RecordedAt)
{
    public static ProductionShiftSummaryDto FromDomain(
        ProductionShift shift,
        IReadOnlyDictionary<string, string> categoryByCode) => new(
        Id: shift.Id,
        MachineCode: shift.MachineCode.Value,
        Window: new WindowDto(shift.Window.Start, shift.Window.End),
        TotalParts: shift.Totals.TotalParts,
        ApprovedParts: shift.Totals.ApprovedParts,
        RejectedParts: shift.Totals.RejectedParts,
        DowntimeBreakdown: shift.Downtime.Entries
            .Select(kvp => new DowntimeEntryDto(
                kvp.Key.Value,
                categoryByCode.GetValueOrDefault(kvp.Key.Value, "UNPLANNED"),
                kvp.Value.Seconds))
            .OrderByDescending(e => e.Seconds)
            .ToList(),
        Availability: Math.Round(shift.Metrics.Availability.Value, 4),
        Performance: Math.Round(shift.Metrics.Performance.Value, 4),
        Quality: Math.Round(shift.Metrics.Quality.Value, 4),
        Oee: Math.Round(shift.Metrics.OverallOee.Value, 4),
        SourceIngestionId: shift.SourceIngestionId,
        SupersededBy: shift.SupersededBy,
        RecordedAt: shift.RecordedAt);
}