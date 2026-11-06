using FluentAssertions;
using OEESentinel.Domain.ValueObjects;

namespace OEESentinel.UnitTests.Domain.ValueObjects;

public class EmailAddressTests
{
    [Fact]
    public void Constructor_WithValidEmail_NormalizesToLowercase()
    {
        var email = new EmailAddress("Admin@OEE-Sentinel.Local");
        email.Value.Should().Be("admin@oee-sentinel.local");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("@missing-local.com")]
    [InlineData("missing-domain@")]
    public void Constructor_WithInvalidFormat_ThrowsArgumentException(string value)
    {
        var act = () => new EmailAddress(value);
        act.Should().Throw<ArgumentException>();
    }
}