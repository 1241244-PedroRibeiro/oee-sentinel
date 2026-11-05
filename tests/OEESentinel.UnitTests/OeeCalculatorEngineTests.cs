using FluentAssertions;
using OEESentinel.Domain.Services;
using OEESentinel.Domain.ValueObjects;
using Xunit;

namespace OEESentinel.UnitTests;

public class OeeCalculatorEngineTests
{
    [Fact]
    public void Calculate_ShouldReturnCorrectOee_WhenShiftHasStandardDowntimeAndScrap()
    {
        // Arrange
        var plannedTime = Duration.FromHours(8); // 28800s
        var downtime = Duration.FromMinutes(48); // 2880s
        var idealCycle = new IdealCycleTime(30.0); // 30s/peça
        var totals = new ProductionTotals(totalParts: 800, approvedParts: 760);

        // Act
        var metrics = OeeCalculatorEngine.Calculate(plannedTime, downtime, totals, idealCycle);

        // Assert
        metrics.Availability.AsPercentage().Should().Be(90.0); // (28800 - 2880) / 28800 = 0.90
        metrics.Performance.AsPercentage().Should().Be(92.59); // (800 * 30) / 25920 = 0.9259
        metrics.Quality.AsPercentage().Should().Be(95.0); // 760 / 800 = 0.95
        metrics.OverallOee.AsPercentage().Should().Be(79.17); // 0.90 * 0.9259 * 0.95
    }

    [Fact]
    public void Calculate_ShouldThrowArgumentOutOfRangeException_WhenDowntimeExceedsPlannedTime()
    {
        // Arrange
        var plannedTime = Duration.FromHours(8);
        var downtime = Duration.FromHours(9); // Paragem maior que o turno
        var idealCycle = new IdealCycleTime(30);
        var totals = new ProductionTotals(100, 100);

        // Act
        var act = () => OeeCalculatorEngine.Calculate(plannedTime, downtime, totals, idealCycle);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Calculate_ShouldReturnNeutralQuality_WhenZeroPartsProduced()
    {
        // Arrange
        var plannedTime = Duration.FromHours(8);
        var downtime = Duration.Zero; 
        var idealCycle = new IdealCycleTime(30);
        var totals = new ProductionTotals(0, 0); // Nada produzido

        // Act
        var metrics = OeeCalculatorEngine.Calculate(plannedTime, downtime, totals, idealCycle);

        // Assert
        metrics.Quality.AsPercentage().Should().Be(100.0); // Qualidade neutra
        metrics.Performance.AsPercentage().Should().Be(0.0);
    }
}