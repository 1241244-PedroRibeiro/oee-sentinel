# Architecture Summary — OEE-Sentinel

Every request in OEE-Sentinel flows through one of **two inbound surfaces** (a Blazor Server circuit or a REST HTTP call) into the **same Application handler**, which invokes the **Domain services and aggregates**, which in turn read and write through **Repository ports** implemented by **EF Core repositories** over **PostgreSQL**.

There is **no separate frontend application** and **no separate API service**. Both surfaces live inside a single ASP.NET Core process (`OEESentinel.WebUI`) so that the OEE math, the streaming parser, and the persisted facts share one runtime, one DI graph, and one model.

```
┌─────────────────────────┐         ┌──────────────────────────┐
│  Blazor Server          │         │  REST API (external)     │
│  (in-process, SignalR)  │         │  (HTTP/JSON, OpenAPI)    │
│  Upload.razor           │         │  POST /api/v1/logs       │
│  MachineDetail.razor    │         │  GET  /api/v1/machines/  │
│  Pareto.razor           │         │       {code}/oee         │
└───────────┬─────────────┘         └────────────┬─────────────┘
            │  injects IHandlerAsync            │  injects IHandlerAsync
            │  (direct DI, no HTTP hop)         │  (HTTP → Program.cs binding)
            ▼                                    ▼
      ┌──────────────────────────────────────────────────┐
      │  OEESentinel.Application                         │
      │  Handlers: ProcessLogFile, GetMachineOee,        │
      │  GetMachineShift, GetDowntimePareto,             │
      │  ListRecentIngestions                            │
      │  Ports: I*Repository, IProductionLogParser,      │
      │  IUnitOfWork                                     │
      └──────────────────────┬───────────────────────────┘
                             ▼
      ┌──────────────────────────────────────────────────┐
      │  OEESentinel.Domain                              │
      │  Aggregates: Machine, ProductionShift,           │
      │  LogIngestion, DowntimeReason, User              │
      │  Value Objects: OeeMetrics, ProductionTotals,    │
      │  DowntimeBreakdown, Percentage, Duration,        │
      │  IdealCycleTime, TimeRange, MachineCode, …       │
      │  Domain Service: OeeCalculatorEngine (pure)      │
      └──────────────────────┬───────────────────────────┘
                             ▼
      ┌──────────────────────────────────────────────────┐
      │  OEESentinel.Infrastructure                      │
      │  CsvProductionLogParser (CsvHelper)              │
      │  EfMachineRepository, EfProductionShiftRepository│
      │  EfLogIngestionRepository,                       │
      │  EfProductionEventRepository,                    │
      │  EfDowntimeReasonRepository, EfUnitOfWork        │
      │  OEESentinelDbContext (EF Core 10)               │
      └──────────────────────┬───────────────────────────┘
                             ▼
      ┌──────────────────────────────────────────────────┐
      │  PostgreSQL 16                                   │
      │  machines, downtime_reasons, log_ingestions,     │
      │  production_shifts, production_events, users     │
      └──────────────────────────────────────────────────┘
```

**The Dependency Rule is enforced by `ProjectReference`** (ADR-0001): `Application` depends only on `Domain`; `Infrastructure` depends on `Application` and `Domain`; `WebUI` depends on all three. Nothing points outward.

**Baseline vs planned.** Baseline ships **without authentication** — the API and the Blazor dashboard run on a plant's trusted internal network. JWT authentication and RBAC are **Roadmap T2** (see `ROADMAP.md` and `05-key-scenarios.md` §6). Baseline deliberately ships the `User` aggregate and the `users` table to keep the T2 migration purely additive.

---

## 1. Request pipeline — two inbound surfaces, one handler

### 1.1. Blazor path (in-process)

1. User interaction in a Razor component (`Upload.razor`, `MachineDetail.razor`, …) triggers an event.
2. The component injects an Application handler (e.g., `IProcessLogFileHandler`) through constructor DI.
3. The handler orchestrates the use case: validates preconditions, invokes Domain aggregates/services, persists through ports.
4. The component re-renders on the circuit, updating gauges / tables / charts.

**No HTTP hop.** No serialisation round-trip. The DTO types returned by the handler are the C# types bound in the component — compiler-checked end-to-end.

### 1.2. REST path (HTTP)

1. External client (MES/ERP, curl, Bruno) sends `POST /api/v1/logs` or `GET /api/v1/machines/{code}/oee`.
2. `Program.cs` minimal-API handler binds the request (multipart or query) and invokes the **same** Application handler via DI.
3. Domain exceptions are mapped to RFC 9457 `ProblemDetails` at the endpoint boundary.
4. Response is serialised to JSON per `docs/api/openapi.yaml`.

**The two paths converge on the same handler.** A behavioural difference between the two is a bug (`04-principles-and-guidelines.md` §4.2).

### 1.3. Full request lifecycle (example: `POST /api/v1/logs`)

| Step | Layer | Component | Responsibility |
|---|---|---|---|
| 1 | WebUI | `Program.cs` minimal-API endpoint | Bind `[FromForm] LogUploadRequest`, validate `machineCode` and window |
| 2 | Application | `ProcessLogFileHandler` | Orchestrate the use case |
| 3 | Domain | `IMachineRepository.GetByCodeAsync` | Read-only lookup; reject unknown machine |
| 4 | Infrastructure | `EfMachineRepository` | EF Core `AsNoTracking().FirstOrDefaultAsync` |
| 5 | Application | `IProductionLogParser.ParseAsync` | **Port** invocation |
| 6 | Infrastructure | `CsvProductionLogParser` | Stream `IAsyncEnumerable<ProductionEvent>` |
| 7 | Domain | *(accumulation)* `ProductionTotals`, `DowntimeBreakdown` | Constructed from events; invariants enforced |
| 8 | Domain | `OeeCalculatorEngine.Calculate` | Pure OEE math (throws on `planned == 0`, `downtime > planned`) |
| 9 | Domain | `ProductionShift` aggregate | Constructed with computed `OeeMetrics` |
| 10 | Infrastructure | `IProductionEventRepository.BulkInsertAsync` | `EFCore.BulkExtensions` batch insert |
| 11 | Infrastructure | `IProductionShiftRepository.AddAsync` | EF Core `Add` (single row) |
| 12 | Infrastructure | `ILogIngestionRepository.AddAsync` / `markCompleted` | Persist lifecycle |
| 13 | Infrastructure | `IUnitOfWork.CommitAsync` | Single EF Core transaction |
| 14 | WebUI | `Program.cs` endpoint | Map domain exceptions → `ProblemDetails`; return `201` + `Location` |
| 15 | Infrastructure | `EF Core` → `PostgreSQL` | Npgsql over TCP |

**Failure semantics** (which the endpoint must respect):

- **Domain invariant fails at step 8** → `ProductionShift` is **not** written. `LogIngestion` is persisted as `FAILED` with a stable reason code. Endpoint returns `422`.
- **Duplicate window detected** (before step 10) → `ProductionShift` is not written. Endpoint returns `409`.
- **Cancellation during parse (steps 5-7)** → `CancellationToken` propagates through `ParseAsync` and `BulkInsertAsync`. Handler catches and calls `LogIngestion.markFailed`. Transaction rolls back cleanly.
- **Database unavailable** → `NpgsqlException` propagates to `Program.cs`, which maps to `503` or `500` (per `ProblemDetails`). No partial commit.

---

## 2. Service catalogue (Application handlers + Domain services + Infrastructure adapters)

| Service | Type | Responsibility | Consumed by |
|---|---|---|---|
| `ProcessLogFileHandler` | Application | Orchestrates upload → parse → compute → persist. Uses `IProductionLogParser`, `IProductionEventRepository`, `IProductionShiftRepository`, `ILogIngestionRepository`, `IUnitOfWork`, `OeeCalculatorEngine`. | `Upload.razor`; `POST /api/v1/logs` |
| `GetMachineOeeHandler` | Application | Reads non-superseded `ProductionShift` facts in a window; aggregates **from totals** (never averages percentages). | `MachineDetail.razor`, `Trend.razor` (future T4); `GET /api/v1/machines/{code}/oee` |
| `GetMachineShiftHandler` | Application | Reads one shift; maps to `ProductionShiftSummaryDto`. | `ShiftDetail.razor`; `GET /api/v1/machines/{code}/shifts` (single) |
| `ListMachineShiftsHandler` | Application | Paginated list of shift facts. | `MachineDetail.razor`; `GET /api/v1/machines/{code}/shifts` |
| `GetDowntimeParetoHandler` | Application | Reads shifts in a window; aggregates `DowntimeBreakdown` entries in memory; top-20 sorted. | `Pareto.razor`; `GET /api/v1/machines/{code}/pareto` |
| `ListRecentIngestionsHandler` | Application | Reads recent ingestion records. | `Ingestions.razor`; `GET /api/v1/logs/{id}` (single) |
| `OeeCalculatorEngine` | Domain service (pure static) | Computes A/P/Q/OEE. Throws on structural corruption; clamps `[0,1]` defensively. | `ProcessLogFileHandler`; `OeeCalculatorEngineTests` |
| `CsvProductionLogParser` | Infrastructure adapter | Streaming CSV parse via `CsvHelper`; emits `IAsyncEnumerable<ProductionEvent>`. | `ProcessLogFileHandler` (via port) |
| `EfMachineRepository` | Infrastructure adapter | `IMachineRepository` implementation. | `ProcessLogFileHandler`, `GetMachineOeeHandler` |
| `EfProductionShiftRepository` | Infrastructure adapter | `IProductionShiftRepository` implementation. | `ProcessLogFileHandler`, `GetMachineOeeHandler`, `GetMachineShiftHandler`, `GetDowntimeParetoHandler` |
| `EfLogIngestionRepository` | Infrastructure adapter | `ILogIngestionRepository` implementation. | `ProcessLogFileHandler`, `ListRecentIngestionsHandler` |
| `EfProductionEventRepository` | Infrastructure adapter | `IProductionEventRepository` (bulk insert only). | `ProcessLogFileHandler` |
| `EfDowntimeReasonRepository` | Infrastructure adapter | `IDowntimeReasonRepository` implementation. | `ProcessLogFileHandler`, `GetDowntimeParetoHandler` |
| `EfUnitOfWork` | Infrastructure adapter | Transaction boundary (`IUnitOfWork`). | `ProcessLogFileHandler` |
| `OEESentinelDbContext` | Infrastructure | EF Core configuration, `OwnsOne`/`ToJson` mappings, migrations. | All `Ef*Repository` |

**Ports** (declared in `Application`, implemented in `Infrastructure`):

- `IMachineRepository`, `IProductionShiftRepository`, `ILogIngestionRepository`, `IProductionEventRepository`, `IDowntimeReasonRepository` — outbound repository ports.
- `IProductionLogParser` — outbound parser port.
- `IUnitOfWork` — transaction boundary port.
- `IProcessLogFileHandler`, `IGetMachineOeeHandler`, `IGetMachineShiftHandler`, `IListMachineShiftsHandler`, `IGetDowntimeParetoHandler`, `IListRecentIngestionsHandler` — inbound handler ports (consumed by `WebUI`).

No handler depends on a concrete type. No port exposes EF Core or CsvHelper. This is the enforcing property of ADR-0001.

---

## 3. Summary of functionalities

### 3.1. Log ingestion (LOG-01, LOG-02)

- **Streaming CSV ingestion** (`POST /api/v1/logs`, `Upload.razor`): accepts a CSV/TXT file, streams through `CsvProductionLogParser` (bounded memory, per-line rejection), accumulates totals and downtime breakdown, invokes `OeeCalculatorEngine`, persists `ProductionShift`. A 200k-row file is ingested in ≤ 5 s with ≤ 200 MB peak RSS (target; measured in T7).
- **Per-line rejection**: malformed lines (missing field, bad decimal locale, non-integer quantity, unknown error code) increment `LogIngestion.RowsRejected` and are sampled (max 100 entries) into `rejected_sample` (JSONB). The whole batch is never aborted by a single bad line.
- **Ingestion lifecycle**: `PENDING → PROCESSING → COMPLETED | FAILED`. `LogIngestion.markCompleted` / `markFailed` are the only transitions; a completed ingestion's `ProductionShift` is immutable. A failed ingestion's events are discarded (`ON DELETE CASCADE` from `production_events`); a completed ingestion's events are retained for audit.
- **Ingestion history** (`GET /api/v1/logs/{id}`, `Ingestions.razor`): query ingestion status, row counts, and rejection sample.

### 3.2. OEE computation and reads (OEE-01, OEE-02, OEE-03)

- **Shift OEE** (`GET /api/v1/machines/{code}/shifts`, `ShiftDetail.razor`): the four pillars and the downtime breakdown of a single `ProductionShift` fact. The values are **immutable** — every shift is a recorded fact, not a live view.
- **Aggregated OEE trend** (`GET /api/v1/machines/{code}/oee`, `Trend.razor` planned T4): aggregates all non-superseded shifts in a window **from totals**, never by averaging percentages. The four pillars are recomputed from the totals (`A = Σ Operating / Σ Planned`, etc.).
- **Downtime Pareto** (`GET /api/v1/machines/{code}/pareto`, `Pareto.razor`): top-20 downtime reasons by aggregated duration across a window, sorted descending, ties broken by `reasonCode` ascending.
- **Window guard**: `to - from ≤ 90 days`. Larger spans are rejected with `400` to prevent unbounded scans.

### 3.3. OEE math guarantees (per ADR-0006)

- **Clamped outputs**: `Availability`, `Performance`, `Quality`, `OEE ∈ [0,1]`. Defensive — the domain *expects* clean inputs, but clamping prevents a corrupted value from being published as `>100%`.
- **Structural corruption rejected**: `planned == 0` and `downtime > planned` throw `ArgumentOutOfRangeException` from the calculator; the handler maps them to `LogIngestion.FAILED` with stable reason codes (`PLANNED_TIME_ZERO`, `DOWNTIME_OVERFLOW`).
- **Neutral cases**:
  - `TotalParts == 0` → `Quality = 1.0` (no production → neutral, not "zero quality").
  - `operating == 0` → `Availability = 0`, `Performance = 0`.
- **`ProductionTotals` invariant**: `RejectedParts` is a **computed** property (`TotalParts - ApprovedParts`), not a constructor parameter. The class of "total ≠ approved + rejected" bug is eliminated by construction.

### 3.4. Reference data (baseline = seeded; management is planned)

- **Machines**: seeded (`MAQ-01`, `MAQ-02`, `PRESSA-B2`). Management UI (create/update/deactivate) is **planned T2** (requires auth first).
- **Downtime reasons**: seeded (`PLANNED_BREAK`, `PLANNED_MAINTENANCE`, `UNPLANNED_MECHANICAL`, `UNPLANNED_TOOL_CHANGE`, `UNPLANNED_ELECTRICAL`, `UNPLANNED_OPERATOR`). Management UI is **planned T2**.
- **Users**: seeded (baseline has no login; `users` table exists for T2 readiness).

### 3.5. Observability (planned T5)

- **Prometheus `/metrics`** endpoint exposing `oee_shift_global`, `oee_ingestion_total`, `oee_parse_errors_total` counters. Not in baseline; documented in `ROADMAP.md` T5.
- **Health probe** (`GET /api/v1/health`): baseline. Returns app version, database reachability (`SELECT 1` with 2s timeout), uptime.

---

## 4. Summary of REST endpoints

The authoritative contract is [`docs/api/openapi.yaml`](../api/openapi.yaml). Endpoints are grouped here for navigation, not duplicated.

### 4.1. Log ingestion

| Method | Endpoint | Description | Baseline access | Success |
|---|---|---|---|---|
| `POST` | `/api/v1/logs` | Upload CSV/TXT shift log; compute `ProductionShift`; persist. | Open (trusted network) | `201 Created` |
| `GET` | `/api/v1/logs/{ingestionId}` | Retrieve ingestion lifecycle status and rejection sample. | Open | `200 OK` |

### 4.2. Machines

| Method | Endpoint | Description | Baseline access | Success |
|---|---|---|---|---|
| `GET` | `/api/v1/machines` | List active machines (paginated, searchable). | Open | `200 OK` |
| `GET` | `/api/v1/machines/{code}` | Retrieve machine configuration. | Open | `200 OK` |

> Machine management (`POST`/`PUT`/`DELETE`) is **planned T2** with JWT + RBAC.

### 4.3. OEE

| Method | Endpoint | Description | Baseline access | Success |
|---|---|---|---|---|
| `GET` | `/api/v1/machines/{code}/oee` | Aggregated OEE across a window (from totals, not averages). | Open | `200 OK` |
| `GET` | `/api/v1/machines/{code}/shifts` | List individual `ProductionShift` facts. | Open | `200 OK` |
| `GET` | `/api/v1/machines/{code}/pareto` | Downtime Pareto breakdown for the window. | Open | `200 OK` |

### 4.4. Health

| Method | Endpoint | Description | Baseline access | Success |
|---|---|---|---|---|
| `GET` | `/api/v1/health` | Liveness/readiness; returns database reachability. | Open | `200` / `503` |

---

## 5. Cross-cutting concerns

### 5.1. Validation

| Layer | Mechanism | What it protects |
|---|---|---|
| **WebUI (minimal API)** | Request model binding + explicit `Validator` class per DTO | Malformed query params, bad multipart body, invalid UUIDs |
| **Application (handlers)** | Precondition checks (machine exists, window valid, span ≤ 90 days) | Business preconditions that depend on state |
| **Domain (VOs and aggregates)** | Constructor invariants (`Percentage ∈ [0,1]`, `TotalParts ≥ ApprovedParts ≥ 0`) | Data invariants that must never be violated |
| **Database (DDL)** | `CHECK`, `UNIQUE`, FK `ON DELETE RESTRICT`, `GENERATED ALWAYS AS` for `rejected_parts` | Defence-in-depth: a bypass via direct SQL still fails |

### 5.2. Persistence and transactions

- **One `DbContext`** (`OEESentinelDbContext`), one connection pool (Npgsql default).
- **Reads** use `AsNoTracking()` unless the handler will mutate the aggregate.
- **Writes** that touch multiple tables use `IUnitOfWork.BeginTransactionAsync` → `CommitAsync` / `RollbackAsync`. The only baseline multi-table writer is `ProcessLogFileHandler`.
- **Bulk insert** of `production_events` uses `EFCore.BulkExtensions.PostgreSql` with batches of 5000; this bypasses EF Core change tracking for performance.
- **Migrations** are versioned and committed (`dotnet ef migrations add …`). `EnsureCreated()` is prohibited (ADR-0003).

### 5.3. Error mapping (REST)

Domain exceptions thrown by handlers are mapped to `ProblemDetails` at the endpoint boundary:

| Exception | HTTP status | `ProblemDetails.type` |
|---|---|---|
| `MachineNotFoundDomainException` | `404` | `.../machine-not-found` |
| `LogIngestionNotFoundDomainException` | `404` | `.../ingestion-not-found` |
| `InvalidWindowDomainException` | `400` | `.../invalid-window` |
| `DuplicateShiftDomainException` | `409` | `.../shift-already-exists` |
| `PlannedTimeZeroDomainException` | `422` | `.../planned-time-zero` |
| `DowntimeOverflowDomainException` | `422` | `.../downtime-overflow` |
| No shifts in window (empty aggregate) | `422` | `.../no-shifts-in-window` |
| Unhandled exception | `500` | `about:blank` (no stack trace) |

**Blazor errors** display as component-level error cards via `ErrorBoundary` and via the handler's domain exception mapping to a user-facing string.

### 5.4. Testability

- **Domain layer** is pure: unit-tested with `xUnit` + `FluentAssertions`; zero infrastructure.
- **Application handlers** are tested with hand-written fakes (`FakeMachineRepository`, `FakeProductionLogParser`, etc.).
- **Integration tests** (`WebApplicationFactory<Program>` + `Testcontainers.PostgreSql`) are **planned T6** — the baseline does not require Docker to run `dotnet test`.
- **End-to-end tests** are not in scope; the UI is a thin consumer of the Application DTOs.

---

## 6. Baseline vs planned — full traceability

| Capability | Baseline | Roadmap task | ADR / spec |
|---|---|---|---|
| Streaming CSV ingestion | ✅ | — | ADR-0005; `05-key-scenarios.md` §2 |
| OEE math with edge-case policy | ✅ | — | ADR-0006; `OeeCalculatorEngineTests` |
| Immutable `ProductionShift` | ✅ | — | ADR-0006; `07-relational-mapping` §2.2 |
| Shift OEE dashboard | ✅ | — | ADR-0004; `06-api` §3.3 |
| Downtime Pareto (in-memory) | ✅ | — | `05-key-scenarios.md` §4 |
| Ingestion history page | ✅ | — | ADR-0004 |
| REST API | ✅ | — | ADR-0005; `openapi.yaml` |
| Health probe | ✅ | — | `openapi.yaml` |
| JWT auth + RBAC | ❌ | **T2** | `ROADMAP.md`; `05-key-scenarios.md` §6 |
| Machine management UI | ❌ | **T2** | `ROADMAP.md` |
| Folder watcher (drop-folder ingestion) | ❌ | **T3** | `ROADMAP.md` |
| PDF report export | ❌ | **T1** | `ROADMAP.md` |
| Pareto read view (SQL, for scale) | ❌ | **T4** | `07-relational-mapping` §5 |
| Prometheus `/metrics` | ❌ | **T5** | `ROADMAP.md` |
| Expanded integration tests | ❌ | **T6** | `ROADMAP.md` |
| BenchmarkDotNet baseline | ❌ | **T7** | `ROADMAP.md` |
| SAD + C4 diagrams (final) | partial | **T8** | `docs/sad/`, `docs/architecture/` |

---

## 7. References

- KB §7.2 (Clean/Onion, Ports & Adapters), §7.4 (Modular Monolith), §6 (C4 model).
- `docs/sad/01-problem-statement.md`, `02-context.md`, `04-principles-and-guidelines.md`, `05-key-scenarios.md`.
- `docs/domain/domain-model.md`.
- `docs/api/openapi.yaml`.
- `docs/architecture/components-diagram.md` (L1–L4).
- `docs/architecture/use-case-diagram.md`.
- ADR-0001, ADR-0002, ADR-0003, ADR-0004, ADR-0005, ADR-0006, ADR-0007.
- `ROADMAP.md` (T1–T8).