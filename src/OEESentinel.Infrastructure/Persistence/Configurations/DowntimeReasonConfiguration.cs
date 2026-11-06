using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OEESentinel.Domain.Entities;
using OEESentinel.Domain.Enums;
using OEESentinel.Domain.ValueObjects;

namespace OEESentinel.Infrastructure.Persistence.Configurations;

public sealed class DowntimeReasonConfiguration : IEntityTypeConfiguration<DowntimeReason>
{
    public void Configure(EntityTypeBuilder<DowntimeReason> b)
    {
        b.ToTable("downtime_reasons", t =>
        {
            t.HasCheckConstraint("chk_downtime_reasons_category",
                "category IN ('PLANNED','UNPLANNED')");
            t.HasCheckConstraint("chk_downtime_reasons_code_upper",
                "code = UPPER(code)");
        });

        b.HasKey(r => r.Code);

        b.Property(r => r.Code)
            .HasColumnName("code")
            .HasMaxLength(64)
            .HasConversion(
                v => v.Value,
                v => new DowntimeReasonCode(v))
            .IsRequired();

        b.Property(r => r.Description)
            .HasColumnName("description")
            .HasMaxLength(255)
            .IsRequired();

        b.Property(r => r.Category)
            .HasColumnName("category")
            .HasMaxLength(10)
            .HasConversion<string>()
            .IsRequired();

        b.Property(r => r.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        b.Property<DateTimeOffset>("CreatedAt")
            .HasColumnName("created_at")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        b.HasIndex(r => r.Code)
            .HasDatabaseName("uq_downtime_reasons_code_ci")
            .IsUnique();
    }
}