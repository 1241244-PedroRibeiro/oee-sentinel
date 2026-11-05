using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OEESentinel.Infrastructure.Persistence.Entities;
using OEESentinel.Domain.Entities;

namespace OEESentinel.Infrastructure.Persistence.Configurations;

public sealed class ProductionEventRowConfiguration : IEntityTypeConfiguration<ProductionEventRow>
{
    public void Configure(EntityTypeBuilder<ProductionEventRow> b)
    {
        b.ToTable("production_events", t =>
        {
            t.HasCheckConstraint("chk_events_quantity", "quantity >= 0");
            t.HasCheckConstraint("chk_events_line", "source_line_number > 0");
        });

        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(e => e.IngestionId).HasColumnName("ingestion_id").IsRequired();
        b.Property(e => e.RecordedAt).HasColumnName("recorded_at").IsRequired();
        b.Property(e => e.Quantity).HasColumnName("quantity").IsRequired();
        b.Property(e => e.ErrorCode).HasColumnName("error_code").HasMaxLength(64);
        b.Property(e => e.SourceLineNumber).HasColumnName("source_line_number").IsRequired();

        b.HasOne<LogIngestion>()
            .WithMany()
            .HasForeignKey(e => e.IngestionId)
            .HasConstraintName("fk_events_ingestion")
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(e => new { e.IngestionId, e.RecordedAt })
            .HasDatabaseName("idx_production_events_ingestion_recorded");
    }
}