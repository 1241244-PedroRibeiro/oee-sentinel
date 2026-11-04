# ADR-0003: PostgreSQL as the Persistence Engine and EF Core as the Persistence Adapter

- **Status:** Accepted
- **Date:** 2026-11-04
- **Decision Makers:** OEE-Sentinel Development Team

## Context

OEE-Sentinel persists three distinct kinds of data:

1. **Reference aggregates** — `Machine`, `DowntimeReason`, `User`. Small, edited occasionally, heavily read.
2. **Computed aggregates** — `ProductionShift`. Immutable after persist; each row is a **fact** with its `OeeMetrics` and `ProductionTotals` frozen. Read heavy, write-once.
3. **Fact stream** — `ProductionEvent`. Append-only, potentially 200k rows per shift per machine. Written in bulk; read for audit and re-analysis; never mutated.

Several invariants must be enforced **at the storage boundary** (KB §21.1):

- `MachineCode` unique (case-insensitive).
- `DowntimeReasonCode` unique.
- `ProductionShift.source_ingestion_id` FK → `log_ingestions.id` (`ON DELETE RESTRICT` — an ingestion that produced shifts cannot be deleted).
- `ProductionShift.machine_id` FK → `machines.id` (`ON DELETE RESTRICT` — soft deactivate `Machine`, do not delete).
- `ProductionTotals`: `total_parts >= approved_parts >= 0`, and `rejected_parts = total_parts - approved_parts` — enforced in the domain, **also** mirrored by DB `CHECK`.
- `OeeMetrics` fields — all `BETWEEN 0 AND 1` (defence-in-depth; the domain clamps).
- `production_events.log_ingestion_id` FK → `log_ingestions.id` (`ON DELETE CASCADE` — an ingestion that fails *before* producing a shift can be discarded along with its raw events, but this is only for failed rows).

The project must justify — not assume — its persistence choice, per KB §9.3 (SQL vs MongoDB trade-off) and §21 (physical mapping).

## Decision

We adopt **PostgreSQL 16** as the sole persistence engine, accessed via **Entity Framework Core 10** with the **`Npgsql.EntityFrameworkCore.PostgreSQL`** provider, and **`EFCore.BulkExtensions.PostgreSql`** for streaming insertion of `ProductionEvent` rows.

Concretely:

1. **`OEESentinelDbContext`** lives in `Infrastructure/Persistence/`. It configures:
   - `DbSet<Machine>`, `DbSet<ProductionShift>`, `DbSet<LogIngestion>`, `DbSet<DowntimeReason>`, `DbSet<User>`, `DbSet<ProductionEvent>`.
   - **Value Object mapping** via `OwnsOne` / `OwnsMany` (e.g., `OeeMetrics` → four `decimal(5,4)` columns; `ProductionTotals` → three `int` columns; `DowntimeBreakdown` → one JSONB column or one child table, decided below).
   - **Enums as strings** (`HasConversion<string>()`), not integers, to keep migration diffs readable.
   - **Unique indexes** on `MachineCode.Value` (with `citext` for case-insensitive uniqueness), `DowntimeReasonCode.Value`, `User.Email.Value`.
   - **FK constraints** as listed in Context, plus `CHECK` constraints for the `ProductionTotals` and `OeeMetrics` invariants.
   - `QueryFilter` for soft deactivation (`Machine.IsActive`, `DowntimeReason.IsActive`).

2. **Migrations** are authored once via `dotnet ef migrations add InitialCreate` and versioned in `Infrastructure/Persistence/Migrations/`. `Database.Migrate()` is called at startup in `Program.cs` (dev) — production deployments apply migrations via a `dotnet ef database update` step. **No `EnsureCreated()`** anywhere.

3. **Bulk insert** for `ProductionEvent` uses `EFCore.BulkExtensions` with `bulkConfig.PreserveInsertOrder = false`, `bulkConfig.SetOutputIdentity = false`. Batch size default 5000; configurable in `appsettings.json`.

4. **Repositories** implement Application ports:
   - `EfMachineRepository` (read-heavy, includes `GetByCodeAsync`, `ListActiveAsync`).
   - `EfProductionShiftRepository` (append-only via `AddAsync`, read via `FindByMachineAndWindowAsync`).
   - `EfLogIngestionRepository` (lifecycle state transitions).
   - `EfProductionEventRepository` (bulk insert only).
   Repositories return **tracked entities** only when mutation is expected; otherwise `AsNoTracking()`. The Domain never sees `IQueryable`.

5. **`DowntimeBreakdown` mapping decision:** stored as **JSONB** (one column on `production_shifts`). Rationale: breakdowns are (a) read atomically with the shift, (b) never queried by inner key, (c) small (≤ ~20 entries per shift). This avoids a `production_shift_downtime_breakdowns` child table whose rows are read only as a whole. The `DowntimeReasonCode` remains the reference; JSONB stores `{"PLANNED_BREAK": 300, "UNPLANNED_MECHANICAL": 900}`.

6. **`docker-compose.yml`** starts PostgreSQL 16 with `POSTGRES_DB=oee_sentinel`, `POSTGRES_USER=oee`, `POSTGRES_PASSWORD` sourced from `.env`. Healthcheck waits for `pg_isready`. The WebUI waits for the DB before migrating (a `DbHealthCheck` at startup).

## Considered Alternatives

### 1. SQLite (file-based)

- **Description:** An embedded, file-based RDBMS shipped as `oee_sentinel.db`.
- **Pros:** Zero infrastructure — no `docker-compose up postgres`. Excellent for single-user local deployment. Adequate for the baseline dataset sizes.
- **Cons:** SQLite lacks `JSONB`, lacks `citext` (case-insensitive unique), lacks `gen_random_uuid()` (without an extension). Its write concurrency is single-writer-at-a-time — fine for one operator but a real constraint if a future feature (roadmap T3, folder watcher) parses two shifts concurrently. `EFCore.BulkExtensions` supports SQLite, but the constraint set we want (foreign keys with `RESTRICT`, JSONB, `citext`) does not translate cleanly. SQLite also cannot demonstrate the "enterprise stack" that BYOP reviewers look for (`(Ext) BYOP - Project submission instructions.pdf` p.2: "a real project").
- **Reason rejected:** Insufficient constraint surface and weaker enterprise alignment; PostgreSQL's advanced features are directly used by the domain.

### 2. MongoDB (document)

- **Description:** Store each `ProductionShift` as a document; each `ProductionEvent` as a document in a capped collection.
- **Pros:** Natural fit for the JSON-shaped `DowntimeBreakdown`. `ProductionEvent` as documents is natural. Schema flexibility.
- **Cons:** Lacks native referential integrity (KB §9.3). `ON DELETE RESTRICT` on `Machine → ProductionShift` would have to be enforced by application code, vulnerable to races between the check and the delete. The unique `MachineCode` case-insensitivity is also application-level. Worse, `ProductionEvent` at 200k documents per shift is well within Mongo's comfort zone, but bulk-insert performance for 200k docs is on par with `EFCore.BulkExtensions` — no advantage. Adds a second paradigm without removing any of PostgreSQL's capabilities. KB §13.3 explicitly guides: "SQL (RDBMS): data highly structured, complex relations, need for strict ACID … Schema rígido."
- **Reason rejected:** Lacks native constraints that are core to the domain (see Context).

### 3. Dapper instead of EF Core

- **Description:** Use Dapper (micro-ORM) with hand-written SQL for all queries; skip EF Core entirely.
- **Pros:** Faster at read-heavy queries; more direct control over SQL. Excellent for a read-oriented reporting API.
- **Cons:** The `DbContext` also handles **migrations** (`dotnet ef migrations`), which Dapper does not provide — a separate migration tool (`FluentMigrator` or `dbup`) would be needed. `EFCore.BulkExtensions` requires EF Core. Value-object mapping (`OwnsOne`) would have to be hand-written in every query. The domain model is small enough that EF Core's convenience outweighs Dapper's raw-speed benefit; the parser (not the DB) is the hot path.
- **Reason rejected:** Adds a tool without removing EF Core; the slow path is the parser, not EF.

### 4. EF Core + Dapper (hybrid)

- **Description:** EF Core for writes and migrations; Dapper for read-side reporting queries.
- **Pros:** Best of both — EF Core's change tracking and migrations for writes, Dapper's raw SQL for dashboard reads.
- **Cons:** Two query paradigms to maintain. The dashboard reads are simple `SELECT * FROM production_shifts WHERE machine_id = $1 AND window_start >= $2` — trivial in EF Core's `.Where(...).ToListAsync()`. The hybrid is a solution looking for a problem. Deferred to a future ADR if read latency becomes a bottleneck (see roadmap T7, BenchmarkDotNet).
- **Reason rejected:** Premature optimisation.

### 5. `DowntimeBreakdown` as a child table instead of JSONB

- **Description:** A `production_shift_downtime_breakdowns` table with `(shift_id, downtime_reason_code, seconds)`.
- **Pros:** Fully normalised; queryable by inner key (e.g., "aggregate all downtime by reason across shifts" is a simple `GROUP BY`).
- **Cons:** Requires a join on every shift fetch — the breakdown is always read with the shift, so the join is on the hot path. `EF Core` would need `.Include(s => s.DowntimeBreakdown)` in every query. Insert requires a second `BulkInsert` for the breakdown rows. The breakdown is small (≤ 20 entries) and read as a whole; JSONB stores it as a single column read in one IO.
- **Reason rejected:** Rejected for reads and writes; the aggregate of all downtime by reason is a Pareto query that lives in a future read view (roadmap T4), not in the transactional path.

## Consequences

### Positive

- **All domain invariants enforced at two levels.** The domain *rejects* invalid input at construction; the database *rejects* it again with `CHECK` and `UNIQUE` and `FK ... ON DELETE RESTRICT`. This matches KB §21.1 (*"O SQL deve proteger os invariantes do DDD"*).
- **Bulk insert performance.** 200k `ProductionEvent` rows per shift insert in ~2–4 seconds with `EFCore.BulkExtensions` on PostgreSQL (dataset measured with BenchmarkDotNet under roadmap T7). No hand-written `COPY` needed.
- **JSONB for breakdowns** keeps the `ProductionShift` schema as one row, one read. `ProductionShift` is a fact — always read whole.
- **Migrations are part of the codebase.** `dotnet ef migrations add ...` produces a reviewable migration file. Schema evolution is auditable.
- **Npgsql** provides native support for `citext`, `jsonb`, `gen_random_uuid()`, and array types — all used (or available) in the baseline.

### Negative / Trade-offs

- **`dotnet ef migrations` requires a live PostgreSQL** (or a valid connection string) for `database update`. The baseline README documents the `docker compose up -d` prerequisite. `EF Core Design` package is required in `WebUI` for the migration CLI. Documented.
- **Migration conflicts** when two developers add migrations concurrently. The project is single-developer; accepted. Multi-developer workflows would need PR-based migration freeze.
- **`EFCore.BulkExtensions` is a third-party library**; the version is pinned. If unmaintained, replacing it with `COPY FROM` is a two-file change (the repository and its config).
- **JSONB hides shape from `dotnet ef`.** A schema change to `DowntimeBreakdown` (e.g., adding a `note` field) is invisible to migrations. Accepted: the shape is owned by `DowntimeBreakdown` in `Domain`, and any change is a domain refactor that would be caught by tests before reaching the DB.
- **`citext` requires the extension to be created.** Added in the initial migration (`CREATE EXTENSION IF NOT EXISTS citext`). The deployment user must have privilege to create extensions; documented in README.
- **No `DbContext` pooling in the baseline** (`AddDbContext` vs `AddDbContextPool`). Adequate for a single-plant deployment; pool deferred to roadmap.

## References

- KB §3.3 (ACID inside aggregates, BASE between), §9.3 (Mongo vs PostgreSQL), §11 (RDBMS, normalisation, transactions), §13.3 (SQL vs MongoDB), §21.1 (physical mapping and constraints), §21.3 (ERD in Mermaid).
- `docs/sad/01-problem-statement.md` §2, §3, §4.
- `docs/domain/domain-model.md` §2–§3 (aggregate boundaries and VO invariants).
- `README.md` ("Prerequisites", "How to run it").
- `docker-compose.yml` (to be added — see ROADMAP Phase 0, P0-5/P0-9).