using OEESentinel.Domain.ValueObjects;

namespace OEESentinel.Domain.Entities;

public sealed class ProductionShift
{
    public Guid Id { get; private set; }
    public MachineCode MachineCode { get; private set; } = default!;
    public TimeRange Window { get; private set; } = default!;
    public ProductionTotals Totals { get; private set; } = default!;
    public DowntimeBreakdown Downtime { get; private set; } = default!;
    public OeeMetrics Metrics { get; private set; } = default!;
    public Guid SourceIngestionId { get; private set; }
    public Guid? SupersededBy { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }

    private ProductionShift() { }

    public static ProductionShift Create(
        MachineCode machineCode,
        TimeRange window,
        ProductionTotals totals,
        DowntimeBreakdown downtime,
        OeeMetrics metrics,
        Guid sourceIngestionId,
        DateTimeOffset recordedAt)
    {
        return new ProductionShift
        {
            Id = Guid.NewGuid(),
            MachineCode = machineCode ?? throw new ArgumentNullException(nameof(machineCode)),
            Window = window ?? throw new ArgumentNullException(nameof(window)),
            Totals = totals ?? throw new ArgumentNullException(nameof(totals)),
            Downtime = downtime ?? throw new ArgumentNullException(nameof(downtime)),
            Metrics = metrics ?? throw new ArgumentNullException(nameof(metrics)),
            SourceIngestionId = sourceIngestionId,
            RecordedAt = recordedAt
        };
    }

    /// <summary>
    /// Sole permitted mutation after persist (ADR-0006 §Decision.2 addendum).
    /// Only the correction workflow (Roadmap T4) may invoke this.
    /// </summary>
    public void MarkSupersededBy(Guid newShiftId)
    {
        if (newShiftId == Id)
            throw new InvalidOperationException("A shift cannot supersede itself.");
        SupersededBy = newShiftId;
    }
}