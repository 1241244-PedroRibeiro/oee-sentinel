# Relational Mapping & Persistence Strategy — OEE-Sentinel

## 1. Executive Summary and Scope

In accordance with ADR-0003 (PostgreSQL + EF Core + `EFCore.BulkExtensions`) and ADR-0006 (immutable facts, Value Objects), this document formalises the mapping strategy between the DDD model (`docs/domain/domain-model.md`) and the physical PostgreSQL schema (`08-database-structure.md`).

Three layers of concern are kept separate:

1. **Domain purity.** Domain entities and Value Objects have **zero persistence annotations**. `ProductionShift` knows nothing of `[Column]`, `[Table]`, or `DbContext`. The `OEESentinelDbContext` lives entirely in `Infrastructure`.
2. **Explicit configuration.** EF Core mapping is configured via `IEntityTypeConfiguration<T>` classes in `Infrastructure/Persistence/Configurations/`, one per aggregate. `OwnsOne` / `OwnsMany` / `ToJson` are the tools used. No fluent-magic beyond the minimum.
3. **Database-level invariants.** Critical invariants are mirrored in DDL: FK constraints with `ON DELETE RESTRICT`, CHECK constraints for ranges and consistency, UNIQUE indexes for business codes, and a partial unique index for "one current shift per window".

This document addresses **how** the mapping is done; `08-database-structure.md` addresses **what** the resulting tables look like.

---

## 2. Global Mapping Patterns (DDD → Relational)

```
+-------------------------------------+          +-------------------------------------+
|            DOMAIN LAYER             |          |         PERSISTENCE LAYER           |
|                                     |          |                                     |
|  +-------------------------------+  |  mapper  |  +-------------------------------+  |
|  |   Aggregate Root (pure C#)    |  |<-------->|  |   EF Core configuration        |  |
|  |   - no attributes             |  |  in Infr |  |   - OwnsOne / OwnsMany / ToJson |  |
|  |   - rich behaviour            |  |          |  |   - HasConversion for enums     |  |
|  |   - value objects embedded    |  |          |  |   - HasIndex, HasCheckConstraint|  |
|  +-------------------------------+  |          |  +-------------------------------+  |
|                                     |          |                                     |
+-------------------------------------+          +-------------------------------------+
                                                              |
                                                              ▼
                                          +-------------------------------------+
                                          |        PostgreSQL 16                |
                                          |  - ON DELETE RESTRICT (reference)   |
                                          |  - ON DELETE CASCADE (owned)        |
                                          |  - CHECK constraints on ranges      |
                                          |  - JSONB for DowntimeBreakdown      |
                                          |  - Partial unique index on current  |
                                          +-------------------------------------+
```

### 2.1. Identity Strategy

- **Business ID as primary key.** `Machine` is keyed by `MachineCode` (VARCHAR, uppercase, 2-32 chars). `DowntimeReason` by `DowntimeReasonCode` (VARCHAR, uppercase, 3-64 chars). This is a deliberate choice: the code appears in logs, in MES messages, and in the URL (`/api/v1/machines/MAQ-01`). An artificial UUID would force a lookup at every boundary.
- **Technical UUIDs for facts.** `ProductionShift`, `LogIngestion`, and `ProductionEvent` are keyed by `UUID` (v7, time-ordered for locality). Facts do not have a business code — they are identified by `(machine, window)` or by ingestion execution.
- **Composite keys where they naturally arise.** The `shift_supersessions` join table is keyed by `(old_shift_id, new_shift_id)` — a natural pair.
- **No surrogate auto-increment IDs** except `production_events.id` (BIGSERIAL) — the bulk-insert volume makes UUIDs' random-write pattern a measurable cost. Justified in §2.4.

### 2.2. Value Object Mapping

Value Objects are mapped using one of three strategies, depending on cardinality and read pattern:

| VO | Strategy | Columns / shape |
|---|---|---|
| **`Percentage`** | inline scalar | `NUMERIC(5,4)`, `CHECK (v >= 0 AND v <= 1)` |
| **`Duration`** | inline scalar | `DOUBLE PRECISION`, `CHECK (v >= 0)` |
| **`IdealCycleTime`** | inline scalar | `DOUBLE PRECISION`, `CHECK (v > 0)` |
| **`MachineCode`** | inline scalar | `VARCHAR(32)`, `CHECK (v = UPPER(v))`, UNIQUE |
| **`EmailAddress`** | inline scalar | `VARCHAR(255)`, `CHECK (v = LOWER(v))` |
| **`TimeRange`** | flattened composite | `window_start TIMESTAMPTZ`, `window_end TIMESTAMPTZ`, `CHECK (window_start < window_end)` |
| **`ProductionTotals`** | flattened composite | `total_parts INT`, `approved_parts INT`; `rejected_parts` is **computed** (`GENERATED ALWAYS AS (total_parts - approved_parts) STORED`), not stored as input |
| **`OeeMetrics`** | flattened composite (4 columns) | `availability`, `performance`, `quality`, `oee`, each `NUMERIC(5,4)` with range checks |
| **`DowntimeBreakdown`** | **JSONB** | `downtime_breakdown JSONB`, shape `{ "REASON_CODE": seconds, ... }` — read whole with the shift, never queried by inner key |

**Why JSONB for `DowntimeBreakdown`:** the breakdown is (a) always read with the shift, (b) never queried by individual reason code (Pareto aggregation is done in memory — Scenario 4), (c) small (≤ 20 entries). A child table would require a `JOIN` on every shift fetch and a second `BulkInsert` per shift creation. JSONB is a single-column read/write. Deferred to roadmap T4 if Pareto queries become SQL-bound.

**EF Core configuration** (excerpt):

```csharp
public sealed class ProductionShiftConfiguration : IEntityTypeConfiguration<ProductionShift>
{
    public void Configure(EntityTypeBuilder<ProductionShift> b)
    {
        b.ToTable("production_shifts");
        b.HasKey(s => s.Id);

        b.Property(s => s.Id).HasColumnName("id");
        b.Property(s => s.SourceIngestionId).HasColumnName("source_ingestion_id");
        b.Property(s => s.SupersededBy).HasColumnName("superseded_by_id");
        b.Property(s => s.RecordedAt).HasColumnName("recorded_at");

        b.OwnsOne(s => s.Window, w =>
        {
            w.Property(x => x.Start).HasColumnName("window_start");
            w.Property(x => x.End).HasColumnName("window_end");
        });

        b.OwnsOne(s => s.Totals, t =>
        {
            t.Property(x => x.TotalParts).HasColumnName("total_parts");
            t.Property(x => x.ApprovedParts).HasColumnName("approved_parts");
            t.Ignore(x => x.RejectedParts); // computed in DB
        });

        b.OwnsOne(s => s.Metrics, m =>
        {
            m.Property(x => x.Availability).HasColumnName("availability").HasConversion<double>();
            m.Property(x => x.Performance).HasColumnName("performance").HasConversion<double>();
            m.Property(x => x.Quality).HasColumnName("quality").HasConversion<double>();
            m.Property(x => x.GlobalOee).HasColumnName("oee").HasConversion<double>();
        });

        b.OwnsOne(s => s.Downtime, d =>
        {
            d.ToJson("downtime_breakdown");
            d.Property(x => x.Entries); // Dictionary<DowntimeReasonCode, Duration>
        });

        b.HasOne<Machine>()
            .WithMany()
            .HasForeignKey(s => s.MachineCode)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<LogIngestion>()
            .WithMany()
            .HasForeignKey(s => s.SourceIngestionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Partial unique index is created via raw SQL in the migration:
        // CREATE UNIQUE INDEX uq_production_shifts_current
        //   ON production_shifts (machine_code, window_start, window_end)
        //   WHERE superseded_by_id IS NULL;
    }
}
```

### 2.3. Cross-Aggregate References by ID Only

Aggregates never hold object references to other aggregates (ADR-0001, ADR-0006). At the relational level:

- `ProductionShift.machine_code` → `machines.code` FK, `ON DELETE RESTRICT`.
- `ProductionShift.source_ingestion_id` → `log_ingestions.id` FK, `ON DELETE RESTRICT`.
- `ProductionEvent.ingestion_id` → `log_ingestions.id` FK, `ON DELETE CASCADE` (only for a `FAILED` ingestion — a successful ingestion's raw events are retained for audit).
- `LogIngestion.machine_code` → `machines.code` FK, `ON DELETE RESTRICT`.

`ON DELETE RESTRICT` on reference aggregates preserves audit trails. A `Machine` cannot be deleted while it has any `ProductionShift`. Soft-deactivation (`machine.deactivate()`) is the supported operation.

`ON DELETE CASCADE` on `production_events` from `log_ingestions` is a **narrow exception**: it applies only to `FAILED` ingestions, enforced by the application layer (`LogIngestion.markFailed` before any successful shift is produced). A `COMPLETED` ingestion's raw events must survive. This is enforced by the `IProductionEventRepository` never cascading on success.

### 2.4. `production_events` — bulk insert, BIGSERIAL

`production_events` is the highest-volume table (200k rows/shift × 21 shifts/week × 12 machines ≈ 50M rows/year). Decisions:

- **`id BIGSERIAL`** — sequential integer, not UUID. Rationale: the volume makes UUID v4 random-writes a measurable index fragmentation cost (UUID v7 would mitigate, but PostgreSQL's `uuidv7()` is new enough that Npgsql support is not baseline-stable). The event has no business identity; the sequential ID is sufficient for internal reference.
- **`EFCore.BulkExtensions.PostgreSql`** for insert. `BulkInsertAsync(list, config: { PreserveInsertOrder = false, SetOutputIdentity = false })`. Batches of 5000 rows. Measured at ~2-4 s for 200k rows on the reference machine (T7 will formalise the benchmark).
- **No FK to `machines`** on `production_events` (denormalised). Rationale: the ingestion already has the FK; the event needs a fast insert path without an FK lookup. The `ingestion_id` FK cascades validation implicitly.
- **Index strategy:** `(ingestion_id, recorded_at)` composite index for re-analysis queries (e.g., "re-parse an ingestion"). Not indexed on `machine_code` because the join is via `ingestion_id`.

### 2.5. Transaction Boundaries

Every write operation that touches more than one table is wrapped in an EF Core transaction (`dbContext.Database.BeginTransactionAsync`). Specifically:

| Operation | Tables touched | Transaction |
|---|---|---|
| `POST /api/v1/logs` (success) | `log_ingestions`, `production_events`, `production_shifts` | Single transaction, committed atomically |
| `POST /api/v1/logs` (failed) | `log_ingestions` only (status `FAILED`) | Single transaction |
| Shift correction (future T4) | `production_shifts` (new row + `superseded_by_id` on old) | Single transaction |
| `GET` reads | No transaction (implicit `READ COMMITTED`) | — |

The `IUnitOfWork` port exposes `BeginTransactionAsync` / `CommitAsync` / `RollbackAsync`. `ProcessLogFileHandler` is the primary consumer.

### 2.6. Seeding Strategy

Seeding is **explicit**, not part of migrations:

- `Infrastructure/Persistence/Seeding/BaselineSeed.cs` is a class with `SeedAsync(OEESentinelDbContext)`.
- Called from `Program.cs` **only when** `app.Environment.IsDevelopment()` **and** `SeedData:Enabled = true` in `appsettings.Development.json`.
- Inserts: 3 machines (`MAQ-01`, `MAQ-02`, `PRESSA-B2`), 6 downtime reasons (`PLANNED_BREAK`, `PLANNED_MAINTENANCE`, `UNPLANNED_MECHANICAL`, `UNPLANNED_TOOL_CHANGE`, `UNPLANNED_ELECTRICAL`, `UNPLANNED_OPERATOR`), 1 admin user (baseline only, no auth wired).
- Idempotent: uses `INSERT ... ON CONFLICT DO NOTHING` semantics or checks for existing rows. Re-running the seed is safe.

BYOP compliance: no real customer data — the seed is fictitious (`(Ext) BYOP - Project submission instructions.pdf` p.3 "No real personal or customer data").

---

## 3. Physical Column Naming Convention

- `snake_case` for column names (`window_start`, `source_ingestion_id`, `downtime_breakdown`).
- `PascalCase` for C# property names (`Window.Start`, `SourceIngestionId`).
- EF Core mapping is explicit via `.HasColumnName(...)`. No convention-based naming.
- Enum columns are `VARCHAR` with `HasConversion<string>().HasMaxLength(N)`. CHECK constraint lists the allowed values. `INT` enum codes are prohibited — they are opaque in the DB and break on reorder.

---

## 4. Referential Integrity Matrix

| Source | Target | On Delete | Reason |
|---|---|---|---|
| `log_ingestions.machine_code` | `machines.code` | `RESTRICT` | Machine cannot be deleted with ingestion history |
| `production_shifts.machine_code` | `machines.code` | `RESTRICT` | Machine cannot be deleted with shift history |
| `production_shifts.source_ingestion_id` | `log_ingestions.id` | `RESTRICT` | Ingestion that produced a shift cannot be deleted |
| `production_shifts.superseded_by_id` | `production_shifts.id` | `RESTRICT` | Supersession chain is preserved |
| `production_events.ingestion_id` | `log_ingestions.id` | `CASCADE` | Failed ingestion's events are discardable (see §2.3) |
| `users.role` | (enum, not a table) | n/a | Enum `VARCHAR` |

**No `ON DELETE SET NULL` anywhere.** A production shift without a machine or ingestion is meaningless data. The domain requires `NOT NULL` on all these FKs.

---

## 5. Query Patterns and `AsNoTracking`

| Handler | Query | Tracking |
|---|---|---|
| `GetMachineOeeHandler` | `FindActiveByMachineAndWindowAsync` | `AsNoTracking()` (read-only aggregate) |
| `GetMachineShiftsHandler` | Same, paginated | `AsNoTracking()` |
| `GetDowntimeParetoHandler` | Same | `AsNoTracking()` |
| `ProcessLogFileHandler` | `GetByCodeAsync` (read), then `AddAsync` (write) | Tracked (for the write) |
| `LogIngestionRepository.GetByIdAsync` | Single | Tracked (used for lifecycle transitions) |

`AsNoTracking()` is the **default** for read-side queries. Tracking is opt-in for handlers that will mutate the aggregate in the same `DbContext` scope.

---

## 6. Living Evolution Log

### 6.1. Baseline (submission)

- Tables: `machines`, `downtime_reasons`, `log_ingestions`, `production_shifts`, `production_events`, `users`.
- JSONB: `production_shifts.downtime_breakdown`, `log_ingestions.rejected_sample`.
- Partial unique index: `uq_production_shifts_current`.
- No auth wired; `users` table exists but no login endpoint.

### 6.2. T2 additions (auth)

- `users.password_hash` already present; `users.is_active` already present.
- New endpoint `POST /api/v1/auth/login` (WebUI only).
- No schema change.

### 6.3. T4 additions (Pareto view)

- New SQL view `v_machine_downtime_pareto` if the in-memory aggregation (Scenario 4) becomes a bottleneck. Migration adds the view; read handler switches from in-memory to view.

### 6.4. T3 additions (folder watcher)

- New `background_jobs` table (optional) to track watcher runs. Not required for baseline; may reuse `log_ingestions` with a `triggered_by = WATCHER` column (small migration).

### 6.5. Never planned

- Event sourcing (massively overkill for the domain).
- Read replicas (single-plant deployment).
- Sharding / partitioning (volume is bounded).

---

## 7. Comparative Log

### Section 2.2 — `DowntimeBreakdown` mapping

- **Initial:** "Normalized child table `production_shift_downtimes`."
- **Current:** "JSONB column on `production_shifts`."
- **Justification:** See §2.2. The breakdown is always read whole; the child table adds a `JOIN` and a second `BulkInsert` for zero query benefit at the baseline volume.

### Section 2.4 — `production_events.id`

- **Initial:** "UUID v4 for consistency with `production_shifts.id`."
- **Current:** "`BIGSERIAL`."
- **Justification:** Volume. `production_events` is a fact stream (200k rows per shift × N shifts). UUID v4's random write pattern fragments the B-tree index measurably above ~10M rows. Sequential `BIGSERIAL` is the standard choice for append-only fact tables. The event has no business identity that requires a UUID.

### Section 2.3 — `production_events` FK on `log_ingestions`

- **Initial:** "`ON DELETE CASCADE` on success."
- **Current:** "`ON DELETE CASCADE` only for `FAILED` ingestions (enforced by the application); `RESTRICT` semantics for `COMPLETED`."
- **Justification:** A `COMPLETED` ingestion's raw events must survive for audit and re-analysis (see `05-key-scenarios.md` Scenario 3). An ingestion that failed after partial parsing can have its events discarded. The application enforces this via `markFailed` before any `ProductionShift` is written.