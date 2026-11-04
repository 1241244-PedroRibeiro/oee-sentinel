# C4 Component Diagrams — OEE-Sentinel

## 1. Purpose and Reading Guide

This document is the **narrative companion** to the four PlantUML C4
diagrams in `docs/architecture/components-diagram/`. Per KB §6.2, the C4
model is used as an *"abstraction-first"* map: each level answers one
question, and only one. The diagrams themselves are the primary artifacts;
this document is the **catalogue of elements** (responsibilities,
interfaces, dependencies) that the diagrams cannot carry on their own.

The four levels, and when each is used:

| Level | File | Question | Audience |
|---|---|---|---|
| **L1** | `l1.puml` | *What system are we building and how does it fit the plant?* | Everyone (executives, plant IT, operators). |
| **L2** | `l2.puml` | *What runs inside the boundary?* | Technical leadership, architects. |
| **L3** | `l3.puml` | *What are the responsibilities inside the Web App container?* | Developers. |
| **L4** | `l4.puml` | *How is the Domain layer actually coded?* | Developers, reviewers. |

> **A note on "Container".** In C4, a *container* is a separately deployable
> unit. It is **not** a Docker container (KB §6.2). In OEE-Sentinel the
> entire .NET solution (`WebUI` + `Application` + `Domain` + `Infrastructure`)
> runs as **one process** — one container. PostgreSQL is a second container.
> Adding more containers would be a **container smell** (KB §6.2).

### Anti-patterns explicitly avoided

- **Mixed abstraction levels.** No diagram shows both an external system and an internal component (KB §6.2). L1 stops at "OEE-Sentinel" as a black box. L2 opens it into containers. L3 opens *one* container into components. L4 opens one component into classes.
- **Bare relations.** Every relation has an active verb and, where relevant, a protocol (`"Queries OEE via [JSON/HTTPS]"`, not `"uses"`).
- **No "Backend" mega-box.** The Web App container is specific (`ASP.NET Core 10 / Blazor Server`), not generic.
- **No shared databases without an owner.** PostgreSQL is owned exclusively by the Web App container. No other container reads or writes to it.
- **Code diagrams only where justified.** L4 is limited to the Domain layer — the product — and does not try to document every class in the solution.

---

## 2. Level 1 — System Context (`l1.puml`)

**File:** [`l1.puml`](components-diagram/l1.puml)

### 2.1. Narrative

OEE-Sentinel is a self-hosted analytics engine deployed on a plant's
internal network. Three human actors interact with it through a browser:
Plant Operators (who upload shift logs and check OEE), Production Engineers
(who analyse downtime Pareto and configure machines — the latter planned
T2), and Plant Managers (who review aggregated KPI trends). One external
system — the plant's MES/ERP — consumes OEE results programmatically. A
second external system — Prometheus / enterprise monitoring — is planned
under Roadmap T5 but is included here for completeness.

The system does **not** integrate with real-time SCADA, PLCs, or MQTT
brokers. That is a sibling system's concern (FlowGuard). OEE-Sentinel's
scope is bounded: consume raw log files, compute OEE, expose the result.

### 2.2. Element catalogue

| Element | Type | Responsibility | Interfaces |
|---|---|---|---|
| **Plant Operator** | Person | Uploads shift logs; checks OEE; acknowledges errors. | HTTPS (browser) |
| **Production Engineer** | Person | Analyses Pareto; configures machines (T2); audits ingestion history. | HTTPS (browser) |
| **Plant Manager** | Person | Reviews aggregated trends; drills into shifts. | HTTPS (browser) |
| **OEE-Sentinel** | System (in scope) | Ingests logs, computes A/P/Q/OEE, serves the dashboard and REST API. | Provides: Blazor UI, REST API. Consumes: file uploads. |
| **External MES / ERP** | External system | Consumes OEE results for plant-wide reporting. | Consumes: `GET /api/v1/...` (`JSON/HTTPS`). |
| **Prometheus / Enterprise Monitoring** | External system (planned T5) | Scrapes `/metrics` for dashboarding. | Consumes: `GET /metrics` (OpenMetrics/HTTP). |

### 2.3. Relations

| From | To | Verb | Protocol |
|---|---|---|---|
| Plant Operator | OEE-Sentinel | Uploads logs, views OEE | HTTPS |
| Production Engineer | OEE-Sentinel | Reviews Pareto, configures machines (T2) | HTTPS |
| Plant Manager | OEE-Sentinel | Reviews trends | HTTPS |
| OEE-Sentinel | MES/ERP | Publishes OEE reads *(reverse: MES pulls)* | JSON/HTTPS |
| MES/ERP | OEE-Sentinel | Queries OEE aggregates and shift facts | JSON/HTTPS |
| Prometheus | OEE-Sentinel | Scrapes `/metrics` *(planned T5)* | OpenMetrics/HTTP |

---

## 3. Level 2 — Container (`l2.puml`)

**File:** [`l2.puml`](components-diagram/l2.puml)

### 3.1. Narrative

OEE-Sentinel consists of **one application container** and **one data
container**:

- The **Web App** container is an ASP.NET Core 10 process hosting two
  inbound surfaces (Blazor Server dashboard + REST API) and one outbound
  surface (PostgreSQL via EF Core / Npgsql). It also owns the parser
  (CsvHelper) and the in-process Application handlers.
- The **PostgreSQL** container stores the `machines`, `downtime_reasons`,
  `log_ingestions`, `production_shifts`, `production_events`, and `users`
  tables (see `08-database-structure.md`).

There is **no separate API container** and **no separate parser
service**. These are components *inside* the Web App container (see L3).
This honours the *First Commandment of Distributed Systems* (KB §7.4) — the
baseline does not distribute the parser from the calculator, because they
are on the same hot path.

### 3.2. Container catalogue

| Container | Technology | Responsibility | Storage |
|---|---|---|---|
| **OEE-Sentinel Web App** | ASP.NET Core 10, Blazor Server, .NET 10 | Hosts the dashboard and REST API; orchestrates ingestion; hosts the Application handlers and domain; parses logs; writes to PostgreSQL. | No local persistent state (the container is stateless between requests; only the Blazor circuit holds transient UI state). |
| **OEE-Sentinel Database** | PostgreSQL 16, `EFCore.BulkExtensions.PostgreSql` | Stores reference aggregates, ingestion lifecycle, production facts, and raw production events. | Persistent volume (`pgdata`). |

### 3.3. Relations

| From | To | Verb | Protocol |
|---|---|---|---|
| Plant Operator / Engineer / Manager | Web App | Uses dashboard | HTTPS (Blazor circuit via SignalR) |
| MES/ERP | Web App | Queries OEE aggregates | JSON/HTTPS |
| Prometheus | Web App | Scrapes `/metrics` *(planned T5)* | OpenMetrics/HTTP |
| Web App | Database | Reads/writes facts, reference data, bulk-inserts events | Npgsql over TCP |

**The database is owned exclusively by the Web App container.** No external
system connects to PostgreSQL. A future MES integration goes through the
REST API, not through a direct DB connection.

---

## 4. Level 3 — Component (`l3.puml`)

**File:** [`l3.puml`](components-diagram/l3.puml)

### 4.1. Narrative

The Web App container is partitioned into four logical layers per the
Clean/Onion + Hexagonal style (ADR-0001). Each layer is a component group
in the diagram. The **arrows** in the diagram are the *Dependency Rule*:
they point inward, and only inward. `Infrastructure` implements the ports
declared by `Application`. `WebUI` invokes the `Application` handlers.
`Domain` depends on nothing.

Per KB §6.2, only the *architecturally significant* components are shown.
Utility classes (`DateTimeOffsetExtensions`, `StringSanitizer`) are not
components in the C4 sense and are omitted.

### 4.2. Component catalogue

#### Inbound Adapters (`WebUI`)

| Component | Type | Responsibility | Provides (to) | Requires (from) |
|---|---|---|---|---|
| **Blazor Server Pages** | Razor components (`Upload.razor`, `MachineDetail.razor`, `Pareto.razor`, `Ingestions.razor`, `Home.razor`) | Render the dashboard; bind user input; navigate between pages. | — | Application Handlers (in-process) |
| **REST API Endpoints** | Minimal API / `Program.cs` route handlers | Expose the OpenAPI contract (`docs/api/openapi.yaml`); bind/validate query params and multipart bodies; map domain exceptions to `ProblemDetails`. | — | Application Handlers (in-process) |
| **Composition Root** | `Program.cs` | Wire concrete adapters to ports; configure EF Core, Blazor, OpenAPI; run migrations on startup. | — | All layers (it is the only place allowed to) |

#### Application (`OEESentinel.Application`)

| Component | Type | Responsibility | Provides | Requires |
|---|---|---|---|---|
| **ProcessLogFileHandler** | Application handler | Orchestrate upload → parse → compute → persist. Uses `IProductionLogParser`, `IProductionEventRepository` (bulk), `IProductionShiftRepository`, `ILogIngestionRepository`, `IUnitOfWork`. | `IProcessLogFileHandler` | Ports, Domain |
| **GetMachineOeeHandler** | Application handler | Read non-superseded shifts in a window; aggregate from totals (never average percentages). | `IGetMachineOeeHandler` | `IProductionShiftRepository`, `IMachineRepository` |
| **GetMachineShiftHandler** | Application handler | Read one shift, map to DTO. | `IGetMachineShiftHandler` | `IProductionShiftRepository` |
| **GetDowntimeParetoHandler** | Application handler | Read shifts in a window; aggregate `DowntimeBreakdown` entries in memory; top-20 sorted. | `IGetDowntimeParetoHandler` | `IProductionShiftRepository`, `IDowntimeReasonRepository` |
| **ListRecentIngestionsHandler** | Application handler | Read recent ingestion records. | `IListRecentIngestionsHandler` | `ILogIngestionRepository` |
| **Application Ports** | Interfaces | `IMachineRepository`, `IProductionShiftRepository`, `ILogIngestionRepository`, `IProductionEventRepository`, `IDowntimeReasonRepository`, `IProductionLogParser`, `IUnitOfWork` | (consumed by handlers; implemented by Infrastructure) | — |

#### Domain (`OEESentinel.Domain`)

| Component | Type | Responsibility | Provides | Requires |
|---|---|---|---|---|
| **OeeCalculatorEngine** | Domain service (static) | Pure OEE math. Throws on structural corruption (`planned == 0`, `downtime > planned`). Clamps `[0,1]` defensively. | `OeeMetrics CreateFromCalculation(...)` | `Duration`, `ProductionTotals`, `IdealCycleTime`, `OeeMetrics` (value objects) |
| **Aggregate Roots** | Entities | `Machine`, `ProductionShift`, `LogIngestion`, `DowntimeReason`, `User`. Rich behaviour; no setters. | Public methods per aggregate | Value Objects |
| **Value Objects** | Immutable records | `OeeMetrics`, `ProductionTotals`, `DowntimeBreakdown`, `Percentage`, `Duration`, `IdealCycleTime`, `TimeRange`, `MachineCode`, `EmailAddress`, `DowntimeReasonCode`. | Constructors with validation; immutable properties | — |
| **Enums** | Enumerations | `IngestionStatus`, `DowntimeCategory`, `UserRole` | — | — |

#### Infrastructure (`OEESentinel.Infrastructure`)

| Component | Type | Responsibility | Provides (implements) | Requires |
|---|---|---|---|---|
| **CsvProductionLogParser** | Outbound adapter | Streaming CSV parse via CsvHelper. Emits `IAsyncEnumerable<ProductionEvent>`. Per-line rejection; no whole-file buffering. | `IProductionLogParser` | `CsvHelper`, Domain |
| **EF Core Repositories** | Outbound adapters | `EfMachineRepository`, `EfProductionShiftRepository`, `EfLogIngestionRepository`, `EfProductionEventRepository`, `EfDowntimeReasonRepository`, `EfUnitOfWork` | The five `I*Repository` ports + `IUnitOfWork` | `OEESentinelDbContext` |
| **OEESentinelDbContext** | EF Core `DbContext` | Configure value-object mappings (`OwnsOne`, `ToJson`), enums, indexes, migrations. | — | `Npgsql.EntityFrameworkCore.PostgreSQL` |

### 4.3. Relations (the Dependency Rule)

| From | To | Verb |
|---|---|---|
| Blazor Pages | Application Handlers | Invokes (in-process, direct DI) |
| REST Endpoints | Application Handlers | Invokes (in-process, direct DI) |
| Application Handlers | OeeCalculatorEngine | Computes via |
| Application Handlers | Aggregate Roots | Orchestrates |
| Application Handlers | Application Ports | Depends on (abstraction) |
| Application Ports | *(implemented by)* CsvProductionLogParser, EF Core Repositories | — |
| CsvProductionLogParser | Domain Value Objects / ProductionEvent | Constructs |
| EF Core Repositories | OEESentinelDbContext | Uses |
| OEESentinelDbContext | PostgreSQL | Reads/writes (Npgsql/TCP) |

**Nothing in Application or Domain has a compile-time dependency on
Infrastructure or WebUI.** This is enforced by `ProjectReference` in the
`.csproj` files (ADR-0001).

---

## 5. Level 4 — Code (`l4.puml`)

**File:** [`l4.puml`](components-diagram/l4.puml)

### 5.1. Narrative

Per KB §6.2, Level 4 (Code) is produced **only where complexity justifies
it**. For OEE-Sentinel, the layer whose complexity warrants a class
diagram is the **Domain layer** — because it *is* the product. The OEE
math, the value-object invariants, and the aggregate boundaries are the
parts a reviewer needs to see concretely. The Application handlers,
WebUI, and Infrastructure follow such standard .NET patterns that a class
diagram would add noise.

The diagram shows:

- The **value objects** and their invariants (which map to CHECK
  constraints in `08-database-structure.md`).
- The **aggregate roots** and their cross-aggregate references by typed
  ID (never by object reference).
- The **`OeeCalculatorEngine`** with its documented edge-case policy.
- The **enumerations**.

### 5.2. Key invariants shown at Level 4

| Class | Invariant | Where it is mirrored at the DB |
|---|---|---|
| `Percentage` | `Value ∈ [0,1]` | `CHECK (v BETWEEN 0 AND 1)` on `availability`, `performance`, `quality`, `oee` |
| `Duration` | `Seconds ≥ 0` | `CHECK (v >= 0)` where persisted (downtime entries in JSONB are validated by `fn_validate_downtime_breakdown`) |
| `IdealCycleTime` | `Seconds > 0` | `CHECK (ideal_cycle_seconds > 0)` on `machines` |
| `TimeRange` | `Start < End` | `CHECK (window_start < window_end)` on `production_shifts` |
| `ProductionTotals` | `TotalParts ≥ ApprovedParts ≥ 0` | `CHECK (total_parts >= 0 AND approved_parts >= 0 AND approved_parts <= total_parts)` |
| `OeeMetrics` | All four fields ∈ [0,1] | Same four CHECKs on `production_shifts` |
| `ProductionShift` (aggregate) | Exactly one current per `(machine, window)` | Partial unique index `uq_production_shifts_current` |

### 5.3. What is deliberately *not* shown

- **`ProductionEvent`** is not an aggregate root. It is a domain fact,
  persisted as an append-only row via `IProductionEventRepository`. It
  appears in `domain-model.md` but is not central to L4 (it has no
  behaviour beyond construction).
- **Application handlers and DTOs** — the standard `Handler → Repository →
  DbContext` pattern is not worth a class diagram.
- **EF Core configuration classes** — the `OwnsOne`/`ToJson` mapping is
  documented in `07-relational-mapping-and-persistence-strategy.md` §2.2.
- **Blazor components** — the component tree is small (5 pages + 4 shared
  components) and documented in `04-principles-and-guidelines.md`.

---

## 6. Cross-References to ADRs

| C4 element | ADR |
|---|---|
| Clean/Onion + Hexagonal (L3 layer groups) | **ADR-0001** |
| Container technology choices (ASP.NET Core / Blazor / PostgreSQL) | **ADR-0002**, **ADR-0003**, **ADR-0004** |
| `CsvProductionLogParser` (L3) | **ADR-0005** |
| Domain value objects and `OeeCalculatorEngine` (L4) | **ADR-0006** |
| `MachineOeeAggregate` read policy (L3 `GetMachineOeeHandler`) | `06-api-design-specification.md` §3.3 (referenced by ADR-0003) |
| Testing seams (fakes at ports) | **ADR-0007** |

---

## 7. What the diagrams must *not* be used for

- **Not deployment topology.** The L1/L2 diagrams do not show plant subnets, VLANs, or firewall rules. Deployment view (C4) is a separate diagram if a future reviewer asks for it.
- **Not sequence flows.** The dynamic interaction of a single use case (e.g., UC-01) is documented in `05-key-scenarios.md` as narrative steps. A C4 Dynamic View would be redundant with those scenarios.
- **Not UX design.** Wireframes and mockups are separate artifacts (Blazor pages serve as the spec).

---

## 8. References

- KB §6 (SAD and C4 Model), §7.2 (Clean/Onion), §7.4 (First Commandment of Distributed Systems).
- `docs/sad/01-problem-statement.md` §3.
- `docs/sad/02-context.md` §3, §4.
- `docs/sad/04-principles-and-guidelines.md` (principles the diagrams must respect).
- `docs/sad/05-key-scenarios.md` (dynamic flows that the L1–L4 diagrams statically describe).
- `docs/domain/domain-model.md` (L4 is the C4 form of this).
- `docs/api/openapi.yaml` (contract that L3's REST endpoints must honour).
- ADR-0001 through ADR-0007.