namespace OEESentinel.Domain.ValueObjects;

public record Duration
{
    public double Seconds { get; }
    public static Duration Zero => new(0.0);

    public Duration(double seconds)
    {
        if (seconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(seconds), "Duration cannot be negative.");
        Seconds = seconds;
    }
    public static Duration FromHours(double hours) => new(hours * 3600);
    public static Duration FromMinutes(double minutes) => new(minutes * 60);
}