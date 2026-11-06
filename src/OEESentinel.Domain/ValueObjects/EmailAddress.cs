using System.Text.RegularExpressions;

namespace OEESentinel.Domain.ValueObjects;

public sealed record EmailAddress
{
    private static readonly Regex Pattern =
        new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    public string Value { get; }

    public EmailAddress(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Email cannot be empty.", nameof(value));
        
        var normalized = value.Trim().ToLowerInvariant();
        if (!Pattern.IsMatch(normalized))
            throw new ArgumentException("Invalid email format.", nameof(value));

        Value = normalized;
    }

    public override string ToString() => Value;
}