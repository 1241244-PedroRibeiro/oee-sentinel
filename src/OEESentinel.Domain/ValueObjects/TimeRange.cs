namespace OEESentinel.Domain.ValueObjects;

public sealed record TimeRange
{
    public DateTimeOffset Start { get; }
    public DateTimeOffset End { get; }

    public TimeRange(DateTimeOffset start, DateTimeOffset end)
    {
        if (start >= end)
            throw new ArgumentOutOfRangeException(
                nameof(end),
                "TimeRange.Start must be strictly before TimeRange.End.");

        Start = start;
        End = end;
    }

    public TimeSpan Duration => End - Start;
}