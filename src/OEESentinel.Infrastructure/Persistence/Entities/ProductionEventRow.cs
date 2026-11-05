namespace OEESentinel.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence-only row for production_events. The domain record
/// <see cref="OEESentinel.Domain.Records.ProductionEvent"/> carries no EF concerns (ADR-0005).
/// </summary>
public sealed class ProductionEventRow
{
    public long Id { get; set; }
    public Guid IngestionId { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public int Quantity { get; set; }
    public string? ErrorCode { get; set; }
    public int SourceLineNumber { get; set; }
}