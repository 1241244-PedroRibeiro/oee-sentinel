using FluentAssertions;
using OEESentinel.Domain.ValueObjects;

namespace OEESentinel.UnitTests.Domain.ValueObjects;

public class DurationTests
{
    [Fact]
    public void Constructor_WithValidSeconds_CreatesInstance()
    {
        var d = new Duration(3600);
        d.Seconds.Should().Be(3600);
    }

    [Fact]
    public void Constructor_WithNegativeSeconds_ThrowsArgumentOutOfRangeException()
    {
        var act = () => new Duration(-1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void FromHours_ConvertsCorrectly()
    {
        var d = Duration.FromHours(8);
        d.Seconds.Should().Be(28800);
    }
}