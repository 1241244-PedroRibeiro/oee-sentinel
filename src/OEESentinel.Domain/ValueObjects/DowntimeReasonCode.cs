using System.Text.RegularExpressions;

namespace OEESentinel.Domain.ValueObjects;

public sealed record DowntimeReasonCode
{
    private static readonly Regex Pattern =
        new("^[A-Z_]{3,64}$", RegexOptions.Compiled);

    public string Value { get; }

    public DowntimeReasonCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("DowntimeReasonCode must not be empty.", nameof(value));
        if (!Pattern.IsMatch(value))
            throw new ArgumentException(
                "DowntimeReasonCode must match ^[A-Z_]{3,64}$.", nameof(value));

        Value = value;
    }

    public override string ToString() => Value;
}