using OEESentinel.Domain.Records;

namespace OEESentinel.Application.Ports.Outbound;

public interface IProductionLogParser
{
    /// <summary>
    /// Streams production events from the stream. Each malformed row invokes
    /// <paramref name="onRejected"/> synchronously; the caller bounds the sample (ADR-0005).
    /// </summary>
    IAsyncEnumerable<ProductionEvent> ParseAsync(
        Stream stream,
        Action<RejectedRow> onRejected,
        CancellationToken cancellationToken);
}