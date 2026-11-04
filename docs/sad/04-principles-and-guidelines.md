# Architectural Principles and Guidelines — OEE-Sentinel

## 1. Modular Monolith and Architectural Style

OEE-Sentinel follows a **Modular Monolith** architecture during its baseline and 20-hour roadmap. The vertical slices — `Topology` (Machine, DowntimeReason), `Ingestion` (LogIngestion, ProductionEvent), `Analytics` (ProductionShift, OeeMetrics), `Identity` (User, deferred auth) — are packaged into a **single ASP.NET Core process** (`OEESentinel.WebUI`) and share one PostgreSQL instance.

- **Simplicity over distributed complexity.** OEE-Sentinel runs on a plant's own server, under `docker compose up`, serving tens of users on a trusted network. Distributing parser, calculator, and UI into separate services would add network latency between the parser and the calculator (the two hottest components), distributed transaction failure modes across `LogIngestion` and `ProductionShift` persistence, and the operational overhead of the Outbox pattern (KB §17.2) — which is explicitly listed as roadmap item T3, not baseline.
- **Encapsulated boundaries.** Each module owns its internal domain and persistence components while exposing explicit, typed interfaces for inter-module collaboration. `Topology` exposes `IMachineRepository` (read contract); `Analytics` consumes it but never reaches into `Topology`'s tables.
- **One bounded context.** OEE-Sentinel is one bounded context (production-log-to-OEE), not four. The split into modules is a *code organisation* decision, not a *bounded-context* decision. This is the reason a single shared `OEESentinelDbContext` spans all modules, and no Outbox / Saga / cross-context coordination is needed.

## 2. Separation of Concerns and Layered Architecture

The back-end codebase enforces a rigorous four-layer concentric structure (ADR-0001), each layer addressing one level of abstraction:

- **`OEESentinel.WebUI` — Inbound Adapter.** ASP.NET Core hosting, Blazor Server circuits, minimal-API REST endpoints, `wwwroot/` static assets, and the composition root (`Program.cs`). Translates transport (HTTP, SignalR) into Application handler invocations. Contains no business logic.
- **`OEESentinel.Application` — Use Cases and Ports.** Orchestration of a transactional use case (`ProcessLogFileCommand`, `GetMachineOeeQuery`), DTO mapping, and declaration of *ports* (`IMachineRepository`, `IProductionLogParser`, `IProductionShiftRepository`, `ILogIngestionRepository`). Contains no formulas and no SQL.
- **`OEESentinel.Domain` — Pure Business Policy.** Rich aggregates (`Machine`, `ProductionShift`, `LogIngestion`, `DowntimeReason`, `User`), Value Objects (`OeeMetrics`, `ProductionTotals`, `DowntimeBreakdown`, `Percentage`, `Duration`, `IdealCycleTime`, `TimeRange`, `MachineCode`, `EmailAddress`), and the `OeeCalculatorEngine` domain service. Zero external dependencies.
- **`OEESentinel.Infrastructure` — Outbound Adapter.** EF Core `OEESentinelDbContext`, `Ef*Repository` implementations, `CsvProductionLogParser` (CsvHelper), bulk-insert adapter (`EFCore.BulkExtensions`). Contains no business logic.

**Rationale.** The **product** of OEE-Sentinel is the OEE math. Everything else — HTTP, SignalR, CsvHelper, EF Core — is a delivery mechanism. Layering by concern lets the math be verified by tests that do not require a database, a socket, or a file system. It also lets the parser be swapped (CsvHelper → `Sep` → a future MQTT reader) without touching Domain or Application.

## 3. The Dependency Rule

Source code dependencies point **exclusively inward** (ADR-0001). Enforced mechanically by `ProjectReference` in `.csproj`, not only by discipline.

- **Inner independence.** `Domain` has zero package references beyond `System.*`. `Application` references only `Domain`. Neither may reference `Microsoft.EntityFrameworkCore.*`, `CsvHelper`, `Microsoft.AspNetCore.*`, or `Microsoft.EntityFrameworkCore.BulkExtensions`. A single `using` of a forbidden namespace in Domain/Application is a blocking code-review comment.
- **Control of direction.** `Infrastructure` and `WebUI` depend on `Application` (and transitively on `Domain`) via `ProjectReference`. They implement interfaces declared in `Application` — never the other way around. `WebUI` is the **only** project that has a `Program.cs` and performs dependency injection wiring.
- **Composition root.** `Program.cs` is the only place where concrete adapters are bound to ports. No service locator, no static mutable singleton, no global `ServiceProvider` accessible from Domain/Application.
- **The one exception, documented.** `Program.cs` calls `app.MapBlazorHub()` and `app.MapRazorComponents<App>()`, which are `Microsoft.AspNetCore.*` calls. These live only in `WebUI`. The Domain and Application never see them.

## 4. API-First Design and RESTful Standards

External REST communication follows the level-2 Richardson Maturity Model (ADR-005), targeting MES/ERP integration and future Prometheus scraping.

- **Resource taxonomy.** URIs identify domain concepts as plural nouns (`/api/v1/machines`, `/api/v1/logs`, `/api/v1/machines/{code}/shifts`). RPC-style verbs (`/api/getMachineOee`) are prohibited.
- **Uniform interface.** Standard verbs (`GET`, `POST`, `PUT`, `DELETE`) with standard semantics. `GET` is safe and idempotent. `POST` creates resources (`POST /api/v1/logs` creates a `LogIngestion`), non-idempotent. `DELETE` deactivates (soft) — never physically deletes a `Machine` referenced by a `ProductionShift` (ADR-0003).
- **Status codes.** RFC-compliant codes only: `200`, `201`, `202`, `204`, `400`, `404`, `409`, `422`, `500`. `409` for state conflicts (duplicate `MachineCode`, deleting a referenced `DowntimeReason`). `422` for domain invariant failure (zero planned time, `downtime > planned` — both rejected by `OeeCalculatorEngine`, ADR-0006).
- **Versioned base path.** `/api/v1/...`. Version is *in the URL path*, not a header. Breaking changes go to `/api/v2`. Additive changes (new optional fields) stay in v1.
- **JSON conventions.** `camelCase` for JSON properties. `kebab-case` for URI segments. `application/json` + UTF-8 exclusively. Enums serialised as `UPPER_SNAKE_CASE` strings (e.g., `UNPLANNED`, `COMPLETED`).
- **Blazor Server is orthogonal to the API.** Blazor components inject Application handlers **directly** (`IGetMachineOeeHandler`), bypassing HTTP. The REST API is for external clients. Both paths converge on the same Application layer — see §4.2.

### 4.2. Two inbound surfaces, one application core

```
┌──────────────────────────────┐     ┌──────────────────────────────┐
│  Blazor Server (in-process)  │     │  REST API (HTTP, external)   │
│  MachineDetail.razor         │     │  GET /api/v1/machines/...    │
│      ↓ injects               │     │      ↓ injected at           │
│  IGetMachineOeeHandler       │     │  Program.cs (DI)             │
└──────────────┬───────────────┘     └──────────────┬───────────────┘
               │                                    │
               └───────────┬────────────────────────┘
                           ▼
                ┌────────────────────┐
                │ OEESentinel.       │
                │ Application        │
                │ (handlers, ports)  │
                └────────────────────┘
```

The Blazor path avoids serialisation and an HTTP hop; the REST path exposes the same capability to external systems. **Neither path may contain domain logic.** A discrepancy in behaviour between the two is a bug.

## 5. Domain-Driven Design and Maintainability

- **Ubiquitous language.** All terms in `docs/glossary.md` are used verbatim across code, migrations, DTOs, OpenAPI schemas, and tests (`Availability`, `Performance`, `Quality`, `ProductionShift`, `DowntimeBreakdown`, `LogIngestion`). Portuguese-language synonyms are prohibited in code.
- **Aggregates as consistency boundaries.** `ProductionShift` is a fact — immutable, append-only. `LogIngestion` has a lifecycle. `Machine` and `DowntimeReason` are reference aggregates with soft-deactivation. Cross-aggregate references are always by typed ID, never by object reference.
- **Rich domain, not anemic.** Entities contain behaviour (`machine.deactivate()`, `ingestion.markFailed(reason)`), not mere getters/setters. `OeeCalculatorEngine` is a pure domain service with no I/O, no clock, no dependency injection (ADR-0006).
- **Modular cohesion.** Namespaces are organised by business capability (`OEESentinel.Domain.Analytics.ProductionShift`), not by technical category (`OEESentinel.Domain.Entities`). This reduces coupling when a capability is extended.

## 6. Testability and Quality Assurance

Testability is a **primary architectural driver**, not an afterthought (ADR-0007).

- **Isolated unit tests.** `OeeCalculatorEngineTests` runs in ~200 ms with zero infrastructure. No database, no file system, no HTTP. Every edge case in the OEE math is covered by a `[Fact]` or `[Theory]` with `FluentAssertions`.
- **Application-level unit tests with hand-written fakes.** `ProcessLogFileCommand` tests use `FakeProductionLogParser`, `FakeMachineRepository`, `FakeProductionShiftRepository` — hand-written, not mocked. Behaviour-based, not interaction-based.
- **Integration tests (deferred).** `Testcontainers.PostgreSql` + `WebApplicationFactory<Program>` is a roadmap item (T6). The baseline does not add Docker-in-CI as a prerequisite for `dotnet test`.
- **F.I.R.S.T.** Every test is Fast, Independent, Repeatable, Self-validating, and Timely. No test reads `DateTime.Now` directly; a caller injects `RecordedAt`.

## 7. Data Integrity and Persistence Constraints

Persistence upholds business invariants **at two levels** (ADR-0003, ADR-0006):

- **Domain level.** Value Objects reject invalid state at construction (`Percentage ∈ [0,1]`, `Duration ≥ 0`, `ProductionTotals.TotalParts ≥ ApprovedParts ≥ 0`). The `OeeCalculatorEngine` throws on structural corruption (`planned == 0`, `downtime > planned`), and clamps on values that could exceed `[0,1]` (defensive).
- **Database level.** CHECK constraints (`CHECK (availability BETWEEN 0 AND 1)`), FK constraints with `ON DELETE RESTRICT` on reference aggregates, UNIQUE on `machines.code`, and the JSONB shape constraint on `downtime_breakdown`. A bypass through a direct SQL `INSERT` still fails.
- **Reproducible evolution.** Schema changes are `dotnet ef migrations add`. Migrations are committed and reviewed. `EnsureCreated()` is prohibited. Seeds are in a separate `SeedData` class invoked explicitly (`WebUI` dev config), not part of migrations.

## 8. Incremental Evolution and the 20-Hour Roadmap

The architecture anticipates the roadmap without compromising the baseline (see `ROADMAP.md` T1–T8).

- **Authentication and RBAC (T2).** Baseline runs on a trusted network (ADR-0002 §Context). JWT + `[Authorize(Roles=...)]` are added without touching Domain or Application handlers — the guard pipeline is a `WebUI` concern. `User` aggregate ships in baseline (schema + repository) to allow the migration to land with zero schema changes.
- **Folder watcher (T3).** `FileSystemWatcher` in a `BackgroundService` that re-invokes `ProcessLogFileCommand`. No domain change. It reuses the exact same handler the upload path uses.
- **Prometheus (T5).** `prometheus-net` middleware in `WebUI`. Metrics derived from `IProductionShiftRepository` reads. No new domain concept.
- **PDF export (T1) and Pareto view (T4).** New Application query handlers + a read-side projection. The Pareto view (T4) may introduce a SQL view `v_machine_downtime_pareto` in Infrastructure; it does not touch the write path.
- **`.slnx`** (ADR-0002). The solution file format is version-agnostic to the domain; a future switch to `.sln` (if .NET tooling regresses) is a mechanical one-command change.

## 9. Simplicity and Value over Formalism

Artifacts and patterns are adopted only when they bring tangible value.

- **No AutoMapper.** DTO mapping in Application is hand-written (`ShiftOeeDto.FromDomain(shift)`). A mapping library would add an abstraction whose config is often harder to reason about than the mapper itself, for a bounded set of DTOs.
- **No MediatR.** Handlers are invoked directly by Blazor components or `Program.cs` endpoints. A request/response mediator would add ceremony without solving a problem the baseline has.
- **No repository-per-method.** `IMachineRepository` has two methods (`GetByCodeAsync`, `ListActiveAsync`), not one per query variant. New methods require justification; the default is a `.Where(...)` in the handler.
- **No premature CQRS.** Baseline reads and writes share the same `DbContext`. The Pareto read view (T4) is the first place where read-model separation earns its keep; until then, `AsNoTracking()` suffices.
- **No speculative ports.** Five outbound ports total (three repositories, one parser, one unit-of-work). Adding a sixth requires an ADR amendment.
- **No `.env` file with secrets in the repo.** `.env.example` only. `docker-compose.yml` reads from `.env` (gitignored). BYOP compliance (`(Ext) BYOP - Project submission instructions.pdf` p.3).

## 10. Architectural Traceability and Reasoning

Documentation is an **active tool for thought**, not a compliance artifact.

- **Architecture as argumentation.** Every significant decision has an ADR (ADR-0001 through ADR-0007). Each captures context, alternatives considered, decision, and consequences — including negative trade-offs.
- **C4 diagrams (planned T8).** Context and Container diagrams in Mermaid, versioned in `docs/sad/c4/`. Component and Code diagrams are produced only when a reviewer needs them.
- **Domain Model Rationale.** `docs/domain/domain-model.md` §6 documents *why* each boundary was traced the way it was (e.g., `ProductionEvent` not inside `ProductionShift`, `DowntimeBreakdown` as JSONB, `ProductionShift` immutability).
- **Comparative logs.** When a decision is revised, the previous version is preserved in a "Revision History" section within the ADR — never silently rewritten. The ADR README (`docs/sad/adrs/README.md`) maintains the supersession log.
- **Living index.** `docs/sad/adrs/README.md` is the navigational entry point for reviewers and lists both the accepted baseline ADRs and the deferred ones (T1–T8).