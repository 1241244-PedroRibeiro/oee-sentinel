using OEESentinel.Application.Ports.Outbound;
using OEESentinel.Domain.Records;

namespace OEESentinel.UnitTests.Fakes;

public sealed class FakeProductionLogParser : IProductionLogParser
{
    public List<ProductionEvent> Events { get; } = new();
    public List<RejectedRow> Rejections { get; } = new();
    public bool ThrowCancellationMidStream { get; set; }

    public void AddEvent(ProductionEvent evt) => Events.Add(evt);
    public void AddRejection(RejectedRow row) => Rejections.Add(row);

    public async IAsyncEnumerable<ProductionEvent> ParseAsync(
        Stream stream,
        Action<RejectedRow> onRejected,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var rejection in Rejections)
            onRejected(rejection);

        var emitted = 0;
        foreach (var evt in Events)
        {
            ct.ThrowIfCancellationRequested();
            if (ThrowCancellationMidStream && emitted == Events.Count / 2)
                throw new OperationCanceledException(ct);

            yield return evt;
            emitted++;
            await Task.Yield();
        }
    }
}