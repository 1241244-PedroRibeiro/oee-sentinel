namespace OEESentinel.Domain.ValueObjects;

public sealed record DowntimeBreakdown
{
    private readonly IReadOnlyDictionary<DowntimeReasonCode, Duration> _entries;

    public IReadOnlyDictionary<DowntimeReasonCode, Duration> Entries => _entries;

    public Duration Total => new(_entries.Values.Sum(d => d.Seconds));

    public DowntimeBreakdown(IReadOnlyDictionary<DowntimeReasonCode, Duration> entries)
    {
        _entries = entries ?? throw new ArgumentNullException(nameof(entries));

        foreach (var (code, duration) in entries)
        {
            if (duration.Seconds < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(entries),
                    $"Downtime duration for '{code}' must be non-negative.");
        }
    }

    public static DowntimeBreakdown Empty { get; } =
        new(new Dictionary<DowntimeReasonCode, Duration>());

    public DowntimeBreakdown Add(DowntimeReasonCode code, Duration duration)
    {
        var copy = new Dictionary<DowntimeReasonCode, Duration>(_entries);
        copy[code] = copy.TryGetValue(code, out var existing)
            ? new Duration(existing.Seconds + duration.Seconds)
            : duration;

        return new DowntimeBreakdown(copy);
    }
}