using OEESentinel.Domain.ValueObjects;

namespace OEESentinel.Domain.Services;

public static class OeeCalculatorEngine
{
    public static OeeMetrics Calculate(
        Duration plannedProductionTime,
        Duration totalDowntime,
        ProductionTotals totals,
        IdealCycleTime idealCycleTime)
    {
        // Edge Case 1: Corrupção de dados (ADR-0006 §Decision.3)
        if (totalDowntime.Seconds > plannedProductionTime.Seconds)
            throw new ArgumentOutOfRangeException(nameof(totalDowntime), "Downtime cannot exceed planned time.");

        double operatingSeconds = plannedProductionTime.Seconds - totalDowntime.Seconds;

        // Edge Case 2: Zero operating time
        if (operatingSeconds == 0)
        {
            return OeeMetrics.Create(
                Percentage.Zero, 
                Percentage.Zero, 
                totals.TotalParts > 0 ? Percentage.Zero : Percentage.One); // Qualidade neutra se nada foi produzido
        }

        // 1. Disponibilidade = Tempo Operacional / Tempo Planeado
        double availability = operatingSeconds / plannedProductionTime.Seconds;

        // 2. Performance = (Total Peças * Tempo Ciclo Ideal) / Tempo Operacional
        double performance = (totals.TotalParts * idealCycleTime.Seconds) / operatingSeconds;
        
        // Edge Case 3: Performance > 1 (Clamp defensivo)
        if (performance > 1.0) performance = 1.0;

        // 3. Qualidade = Peças Boas / Peças Totais
        // Edge Case 4: TotalParts == 0 -> Qualidade = 1.0 (neutra)
        double quality = totals.TotalParts > 0 
            ? (double)totals.ApprovedParts / totals.TotalParts 
            : 1.0;

        return OeeMetrics.Create(
            new Percentage(availability),
            new Percentage(performance),
            new Percentage(quality)
        );
    }
}