using OEESentinel.Domain.Enums;
using OEESentinel.Domain.ValueObjects;

namespace OEESentinel.Domain.Entities;

public sealed class DowntimeReason
{
    public DowntimeReasonCode Code { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public DowntimeCategory Category { get; private set; }
    public bool IsActive { get; private set; }

    private DowntimeReason() { }

    public static DowntimeReason Create(
        DowntimeReasonCode code,
        string description,
        DowntimeCategory category,
        bool isActive = true)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description is required.", nameof(description));

        return new DowntimeReason
        {
            Code = code ?? throw new ArgumentNullException(nameof(code)),
            Description = description,
            Category = category,
            IsActive = isActive
        };
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}