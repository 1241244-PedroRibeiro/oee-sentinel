using FluentAssertions;
using OEESentinel.Domain.ValueObjects;

namespace OEESentinel.UnitTests.Domain.ValueObjects;

public class MachineCodeTests
{
    [Fact]
    public void Constructor_WithValidCode_CreatesInstance()
    {
        var code = new MachineCode("MAQ-01");
        code.Value.Should().Be("MAQ-01");
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("maq-01")]
    [InlineData("MAQ_01")]
    public void Constructor_WithInvalidCode_ThrowsArgumentException(string value)
    {
        var act = () => new MachineCode(value);
        act.Should().Throw<ArgumentException>();
    }
}