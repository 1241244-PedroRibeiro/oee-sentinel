using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OEESentinel.Domain.Entities;
using OEESentinel.Domain.ValueObjects;

namespace OEESentinel.Infrastructure.Persistence.Configurations;

public class MachineConfiguration : IEntityTypeConfiguration<Machine>
{
    public void Configure(EntityTypeBuilder<Machine> builder)
    {
        builder.ToTable("machines");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id).HasColumnName("id");
        builder.Property(m => m.Name).HasColumnName("name").HasMaxLength(150).IsRequired();
        builder.Property(m => m.IsActive).HasColumnName("is_active").IsRequired();

        // Mapeamento dos Value Objects
        builder.Property(m => m.Code)
            .HasConversion(v => v.Value, v => new MachineCode(v))
            .HasColumnName("code")
            .HasMaxLength(32)
            .IsRequired();

        builder.HasIndex(m => m.Code).IsUnique();
        // Nota: Para case-insensitive, o ideal seria usar citext. 
        // Para o baseline, o índice UNIQUE normal serve, mas a KB exige case-insensitive.

        builder.Property(m => m.IdealCycleTime)
            .HasConversion(v => v.Seconds, v => new IdealCycleTime(v))
            .HasColumnName("ideal_cycle_seconds")
            .IsRequired();

        builder.Property(m => m.PlannedShiftDuration)
            .HasConversion(v => v.Seconds, v => new Duration(v))
            .HasColumnName("planned_shift_seconds")
            .IsRequired();
    }
}