using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OEESentinel.Domain.Entities;
using OEESentinel.Domain.ValueObjects;

namespace OEESentinel.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users", t =>
        {
            t.HasCheckConstraint("chk_users_role", "role IN ('OPERATOR','ENGINEER','ADMINISTRATOR')");
        });

        b.HasKey(u => u.Id);
        b.Property(u => u.Id).HasColumnName("id");

        b.Property(u => u.Email)
            .HasColumnName("email")
            .HasMaxLength(255)
            .HasConversion(v => v.Value, v => new EmailAddress(v))
            .IsRequired();

        b.HasIndex(u => u.Email).IsUnique();

        b.Property(u => u.PasswordHash).HasColumnName("password_hash").HasMaxLength(255).IsRequired();
        b.Property(u => u.DisplayName).HasColumnName("display_name").HasMaxLength(100).IsRequired();
        
        b.Property(u => u.Role)
            .HasColumnName("role")
            .HasMaxLength(20)
            .HasConversion<string>()
            .IsRequired();

        b.Property(u => u.IsActive).HasColumnName("is_active").HasDefaultValue(true);

        b.Property<DateTimeOffset>("CreatedAt")
            .HasColumnName("created_at")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");
        
        b.Property<DateTimeOffset>("UpdatedAt")
            .HasColumnName("updated_at")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");
    }
}