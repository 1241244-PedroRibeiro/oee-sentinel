using System.Text.RegularExpressions;

namespace OEESentinel.Domain.ValueObjects;

public record MachineCode
{
    public string Value { get; }
    public MachineCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Code cannot be empty.");
        if (!Regex.IsMatch(value, @"^[A-Z0-9-]{2,32}$")) throw new ArgumentException("Invalid MachineCode format.");
        Value = value;
    }
}