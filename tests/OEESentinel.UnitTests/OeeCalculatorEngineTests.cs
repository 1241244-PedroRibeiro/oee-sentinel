using FluentAssertions;
using OEESentinel.Domain.Services;
using Xunit;

namespace OEESentinel.UnitTests;

public class OeeCalculatorEngineTests
{
    private readonly OeeCalculatorEngine _engine = new();

    [Fact]
    public void Calculate_ShouldReturnCorrectOee_WhenShiftHasStandardDowntimeAndScrap()
    {
        // Turno de 8 horas = 480 minutos
        var plannedTime = TimeSpan.FromHours(8);
        // 48 minutos de paragens registadas
        var downtime = TimeSpan.FromMinutes(48); // Operating Time = 432 min (25920 seg)

        // Máquina concebida para fazer 1 peça a cada 30 segundos
        double idealCycleTime = 30.0;

        // Em 25920s a 30s/peça, o ideal seria 864 peças. Produziu 800 peças:
        int totalUnits = 800;
        int goodUnits = 760; // 40 peças com defeito

        // Act
        var metrics = _engine.Calculate(plannedTime, downtime, totalUnits, goodUnits, idealCycleTime);

        // Assert
        // Disponibilidade = 432 / 480 = 0.90 (90%)
        metrics.AvailabilityPercentage.Should().Be(90.0);

        // Performance = (800 * 30) / 25920 = 24000 / 25920 = ~0.9259 (92.59%)
        metrics.PerformancePercentage.Should().Be(92.59);

        // Qualidade = 760 / 800 = 0.95 (95%)
        metrics.QualityPercentage.Should().Be(95.0);

        // OEE = 0.90 * 0.9259 * 0.95 = ~0.7917 (79.17%)
        metrics.OverallOeePercentage.Should().Be(79.17);
    }

    [Fact]
    public void Calculate_ShouldThrowArgumentException_WhenGoodUnitsExceedTotalUnits()
    {
        var act = () => _engine.Calculate(
            TimeSpan.FromHours(8),
            TimeSpan.Zero,
            totalProducedUnits: 100,
            goodUnits: 150,
            idealCycleTimeSeconds: 10);

        act.Should().Throw<ArgumentException>()
           .WithMessage("*Unidades boas não podem ser negativas nem exceder o total produzido*");
    }
}