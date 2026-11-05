namespace OEESentinel.Domain.ValueObjects;

public sealed record OeeMetrics
{
    public Percentage Availability { get; }
    public Percentage Performance { get; }
    public Percentage Quality { get; }
    public Percentage OverallOee { get; }

    private OeeMetrics(Percentage availability, Percentage performance, Percentage quality)
    {
        Availability = availability;
        Performance = performance;
        Quality = quality;
        // Calculado a partir dos Value Objects
        OverallOee = new Percentage(availability.Value * performance.Value * quality.Value);
    }

    // Apenas o OeeCalculatorEngine pode chamar isto
    public static OeeMetrics Create(Percentage availability, Percentage performance, Percentage quality)
    {
        return new OeeMetrics(availability, performance, quality);
    }
}