namespace OEESentinel.Domain.ValueObjects;

public sealed record OeeMetrics
{
    public double Availability { get; }
    public double Performance { get; }
    public double Quality { get; }
    public double OverallOee { get; }

    public OeeMetrics(double availability, double performance, double quality)
    {
        Availability = Math.Clamp(availability, 0.0, 1.0);
        Performance = Math.Clamp(performance, 0.0, 1.0);
        Quality = Math.Clamp(quality, 0.0, 1.0);
        OverallOee = Math.Round(Availability * Performance * Quality, 4);
    }

    public double AvailabilityPercentage => Math.Round(Availability * 100, 2);
    public double PerformancePercentage => Math.Round(Performance * 100, 2);
    public double QualityPercentage => Math.Round(Quality * 100, 2);
    public double OverallOeePercentage => Math.Round(OverallOee * 100, 2);
}