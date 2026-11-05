using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OEESentinel.Domain.Entities;
using OEESentinel.Domain.Records;
using OEESentinel.Domain.ValueObjects;

namespace OEESentinel.Infrastructure.Persistence.Configurations;

public sealed class LogIngestionConfiguration : IEntityTypeConfiguration<LogIngestion>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private static readonly ValueComparer<IReadOnlyList<RejectedRow>> RejectedComparer = new(
        (a, b) => a!.SequenceEqual(b!),
        v => v.Aggregate(0, (h, r) => HashCode.Combine(h, r.GetHashCode())),
        v => (IReadOnlyList<RejectedRow>)v.ToList());

    public void Configure(EntityTypeBuilder<LogIngestion> b)
    {
        b.ToTable("log_ingestions", t =>
        {
            t.HasCheckConstraint("chk_log_ingestions_status",
                "status IN ('PENDING','PROCESSING','COMPLETED','FAILED')");
            t.HasCheckConstraint("chk_log_ingestions_rows",
                "rows_processed >= 0 AND rows_rejected >= 0 AND rows_rejected <= rows_processed");
            t.HasCheckConstraint("chk_log_ingestions_completed_at",
                "(status = 'COMPLETED' AND completed_at IS NOT NULL) OR (status <> 'COMPLETED' AND completed_at IS NULL)");
            t.HasCheckConstraint("chk_log_ingestions_failure_reason",
                "(status = 'FAILED' AND failure_reason IS NOT NULL) OR (status <> 'FAILED' AND failure_reason IS NULL)");
        });

        b.HasKey(i => i.Id);
        b.Property(i => i.Id).HasColumnName("id");

        b.Property(i => i.MachineCode)
            .HasColumnName("machine_code")
            .HasMaxLength(32)
            .HasConversion(v => v.Value, v => new MachineCode(v))
            .IsRequired();

        b.Property(i => i.OriginalFileName)
            .HasColumnName("original_file_name")
            .HasMaxLength(255)
            .IsRequired();

        b.Property(i => i.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .HasConversion<string>()
            .IsRequired();

        b.Property(i => i.RowsProcessed).HasColumnName("rows_processed").HasDefaultValue(0);
        b.Property(i => i.RowsRejected).HasColumnName("rows_rejected").HasDefaultValue(0);

        b.Property(i => i.RejectedSample)
            .HasColumnName("rejected_sample")
            .HasColumnType("jsonb")
            .HasConversion(
                v => v == null || v.Count == 0
                    ? "[]"                                // ← fix: NULL viola NOT NULL
                    : JsonSerializer.Serialize(v.ToList(), JsonOptions),
                v => string.IsNullOrEmpty(v)
                    ? (IReadOnlyList<RejectedRow>)Array.Empty<RejectedRow>()
                    : JsonSerializer.Deserialize<List<RejectedRow>>(v, JsonOptions)
                      ?? (IReadOnlyList<RejectedRow>)Array.Empty<RejectedRow>(),
                RejectedComparer);

        b.Property(i => i.UploadedAt)
            .HasColumnName("uploaded_at")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        b.Property(i => i.CompletedAt).HasColumnName("completed_at");
        b.Property(i => i.FailureReason).HasColumnName("failure_reason").HasMaxLength(500);

        b.HasOne<Machine>()
            .WithMany()
            .HasForeignKey(i => i.MachineCode)
            .HasPrincipalKey(m => m.Code)
            .HasConstraintName("fk_log_ingestions_machine")
            .OnDelete(DeleteBehavior.Restrict);
    }
}