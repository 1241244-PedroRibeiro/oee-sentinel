using OEESentinel.Domain.Enums;
using OEESentinel.Domain.ValueObjects;

namespace OEESentinel.Domain.Entities;

public sealed class User
{
    public Guid Id { get; private set; }
    public EmailAddress Email { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public string DisplayName { get; private set; } = default!;
    public UserRole Role { get; private set; }
    public bool IsActive { get; private set; }

    private User() { }

    public static User Create(
        EmailAddress email,
        string passwordHash,
        string displayName,
        UserRole role,
        bool isActive = true)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("Password hash is required.", nameof(passwordHash));
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Display name is required.", nameof(displayName));

        return new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = passwordHash,
            DisplayName = displayName,
            Role = role,
            IsActive = isActive
        };
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}