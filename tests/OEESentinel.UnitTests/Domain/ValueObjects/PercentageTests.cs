using FluentAssertions;
using OEESentinel.Domain.ValueObjects;

namespace OEESentinel.UnitTests.Domain.ValueObjects;

public class PercentageTests
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void Constructor_WithValidValues_CreatesInstance(double value)
    {
        var p = new Percentage(value);
        p.Value.Should().Be(value);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void Constructor_WithInvalidValues_ThrowsArgumentOutOfRangeException(double value)
    {
        var act = () => new Percentage(value);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AsPercentage_ReturnsMultipliedByHundred()
    {
        var p = new Percentage(0.756);
        p.AsPercentage().Should().Be(75.6);
    }
}