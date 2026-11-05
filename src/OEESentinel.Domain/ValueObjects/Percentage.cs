namespace OEESentinel.Domain.ValueObjects;

public record Percentage
{
    public double Value { get; }

    public static Percentage Zero => new(0.0);
    public static Percentage One => new(1.0);

    public Percentage(double value)
    {
        if (value < 0.0 || value > 1.0)
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "Percentage must be between 0.0 and 1.0.");

        Value = value;
    }

    public double AsPercentage() => Math.Round(Value * 100, 2);
}