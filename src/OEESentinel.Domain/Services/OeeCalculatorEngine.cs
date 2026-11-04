using OEESentinel.Domain.ValueObjects;

namespace OEESentinel.Domain.Services;

public class OeeCalculatorEngine
{
    public OeeMetrics Calculate(
        TimeSpan plannedProductionTime,
        TimeSpan totalDowntime,
        int totalProducedUnits,
        int goodUnits,
        double idealCycleTimeSeconds)
    {
        if (plannedProductionTime <= TimeSpan.Zero)
            throw new ArgumentException("O tempo planeado de produção deve ser superior a zero.", nameof(plannedProductionTime));

        if (totalProducedUnits < 0)
            throw new ArgumentException("O total de unidades produzidas não pode ser negativo.", nameof(totalProducedUnits));

        if (goodUnits < 0 || goodUnits > totalProducedUnits)
            throw new ArgumentException("Unidades boas não podem ser negativas nem exceder o total produzido.", nameof(goodUnits));

        if (idealCycleTimeSeconds <= 0)
            throw new ArgumentException("O tempo de ciclo ideal deve ser positivo.", nameof(idealCycleTimeSeconds));

        // 1. Disponibilidade = Tempo Operacional Real / Tempo Planeado
        var operatingTime = plannedProductionTime - totalDowntime;
        if (operatingTime < TimeSpan.Zero)
            operatingTime = TimeSpan.Zero;

        double availability = operatingTime.TotalSeconds / plannedProductionTime.TotalSeconds;

        // 2. Performance = (Total Peças * Tempo Ciclo Ideal) / Tempo Operacional
        double performance = 0.0;
        if (operatingTime.TotalSeconds > 0)
        {
            performance = (totalProducedUnits * idealCycleTimeSeconds) / operatingTime.TotalSeconds;
        }

        // 3. Qualidade = Peças Boas / Peças Totais
        double quality = totalProducedUnits > 0
            ? (double)goodUnits / totalProducedUnits
            : 0.0;

        return new OeeMetrics(availability, performance, quality);
    }
}