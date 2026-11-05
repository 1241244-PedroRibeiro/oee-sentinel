namespace OEESentinel.Domain.ValueObjects;

public record IdealCycleTime
{
    public double Seconds { get; }
    public IdealCycleTime(double seconds)
    {
        if (seconds <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(seconds), "Ideal cycle time must be > 0.");
        Seconds = seconds;
    }
}