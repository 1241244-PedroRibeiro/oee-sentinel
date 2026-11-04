# ADR-0001: Layered Clean/Onion Architecture with Hexagonal Ports & Adapters

- **Status:** Accepted
- **Date:** 2026-11-04
- **Decision Makers:** OEE-Sentinel Development Team

## Context

OEE-Sentinel is a production-log analysis engine whose core value lies in a **pure, deterministic, unit-testable OEE calculator** and a **streaming ingestion pipeline**. The business problem (`docs/sad/01-problem-statement.md` §2) is explicitly about consuming legacy industrial log files without loading them into RAM, computing Availability/Performance/Quality across shifts, and preserving an auditable trail of every result. None of these concerns is inherently web- or database-shaped.

However, the engine must also expose a REST API for external MES integration, host a Blazor Server dashboard for operators and plant managers, and persist computed `ProductionShift` results and raw `ProductionEvent` facts into PostgreSQL with `EFCore.BulkExtensions`. The temptation — explicitly warned against by KB §7.1 and §7.4 — is to let the transport (ASP.NET, Blazor), the ORM (EF Core), and the parser (`CsvHelper`) leak their abstractions into the OEE math. That would make the calculator untestable in isolation, force a real PostgreSQL instance to run a single math assertion, and freeze the domain when the parser, the UI, or the database driver changes.

KB §7.2 explicitly mandates *The Dependency Rule*: "as dependências de código fonte apontam exclusivamente para dentro (em direção ao domínio)." KB §3.2 reinforces that Domain entities and Value Objects must remain *rich* (containing behavior, not mere data holders), and KB §3.3 requires aggregates to be designed around **explicit transactional boundaries** — not around what the ORM happens to load lazily.

The stack (`.NET 10`, C#) and the use of `CsvHelper`, `EFCore.BulkExtensions`, and `Blazor Server` are already established in `docs/sad/01-problem-statement.md` §3 and are themselves the subject of ADR-0002. This ADR concerns only the *shape* of the codebase and the direction of its dependencies.

## Decision

We adopt a **Layered Clean/Onion Architecture with Hexagonal Ports & Adapters at the boundaries**.

Concretely:

1. **Concentric layering.** The codebase is partitioned into four layers, each depending only on layers further inward:
   - **`OEESentinel.Domain`** — pure C#. Contains `Machine`, `ProductionShift`, `LogIngestion`, `User`, `DowntimeReason` (Aggregate Roots), `OeeMetrics`, `ProductionTotals`, `DowntimeBreakdown`, `Duration`, `IdealCycleTime`, `TimeRange`, `Percentage`, `MachineCode`, `EmailAddress` (Value Objects), and `OeeCalculatorEngine` (Domain Service). **Zero references** to ASP.NET, EF Core, CsvHelper, Blazor, or `System.IO` abstractions beyond `System` itself.
   - **`OEESentinel.Application`** — use cases and ports. Depends **only** on `Domain`. Contains `ProcessLogFileCommand` + handler, `GetMachineOeeQuery` + handler, DTOs, repository **interfaces** (`IMachineRepository`, `IProductionShiftRepository`, `ILogIngestionRepository`), and the parser port (`IProductionLogParser`). Zero references to EF Core, ASP.NET, or CsvHelper.
   - **`OEESentinel.Infrastructure`** — adapters. Depends on `Domain` and `Application`. Implements the outbound ports: `OEESentinelDbContext` (EF Core), `EfMachineRepository`, `EfProductionShiftRepository`, `CsvProductionLogParser` (CsvHelper), bulk-insert adapters. Contains no business logic.
   - **`OEESentinel.WebUI`** — inbound adapters. Depends on `Application` (and transitively `Domain`). Hosts the Blazor Server circuit, minimal-API / controller endpoints, `wwwroot`, and the composition root (DI wiring in `Program.cs`).

2. **Explicit ports.** Ports are C# interfaces owned by the inner layers:
   - **Inbound ports** (Application, invoked by `WebUI`): `IProcessLogFileHandler`, `IGetMachineOeeHandler`.
   - **Outbound ports** (Application, implemented by `Infrastructure`): `IMachineRepository`, `IProductionShiftRepository`, `ILogIngestionRepository`, `IProductionLogParser`, `IUnitOfWork`.
   Ports are declared only where an architectural boundary exists — not for every internal collaborator.

3. **The Dependency Rule is mandatory.** Domain and Application must not `using` any namespace from `Infrastructure` or `WebUI`. This is enforced *mechanically* by `ProjectReference` in the `.csproj` files (which point only inward) and *by review* (a code review on any PR that introduces a forbidden reference is a blocking comment).

4. **Composition root.** Only `Program.cs` in `WebUI` wires concrete adapters to ports. There is no service locator, no static mutable state, no "god" bootstrap class in Domain/Application.

## Considered Alternatives

### 1. Single-project "layered folders" (Presentation/Application/Domain/Data inside one .csproj)

- **Description:** One C# project with folders named `Controllers/`, `Services/`, `Domain/`, `Repositories/` — the classic N-tier-by-folder layout.
- **Pros:** Minimal `.csproj` ceremony; fastest initial setup; familiar to any .NET developer.
- **Cons:** The compiler cannot enforce the Dependency Rule. Nothing prevents `Domain/Machine.cs` from `using Microsoft.EntityFrameworkCore;`. Testability relies on team discipline rather than on the build. Cross-layer refactors happen silently; the domain gradually accretes ORM attributes (`[Table]`, `[Column]`) that make it impossible to unit-test without EF Core. Violates KB §7.2 ("DAL/Repository must hide persistence completely") and the *Do the thing right* principle in KB §1.1. Fails the first architectural review.
- **Reason rejected:** Fails to mechanically enforce Clean Architecture.

### 2. Vertical Slice Architecture (feature folders, shared nothing)

- **Description:** Each feature (`UploadLog`, `GetMachineOee`, `CreateMachine`) is a self-contained vertical slice with its own controller, handler, request/response models, and query. No shared Domain layer.
- **Pros:** Maximum feature independence; small slices are easy to reason about; adding a feature never touches other features.
- **Cons:** In a domain as **mathematically central** as OEE, vertical slices duplicate the `OeeCalculatorEngine` across every slice that needs it — or, worse, each slice gets its own subtly different version. The three OEE pillars and the clamping invariants are not feature-local; they are the product. Cross-cutting domain invariants (`ProductionTotals.ApprovedParts ≤ TotalParts`) belong in **one** place. KB §3.2 explicitly warns against anemic duplicated models. Vertical slices also complicate the `LogIngestion → ProductionShift` lifecycle, which is inherently a cross-cutting read/write pipeline.
- **Reason rejected:** Incompatible with the *one true math* requirement; duplicates invariants across slices.

### 3. Microservices (separate Domain, Ingestion, Dashboard services)

- **Description:** Decompose into `Domain service` (OEE calculator), `Ingestion service` (parser), `API gateway`, `Dashboard`, each independently deployable.
- **Pros:** Independent scaling; each service can use the language/runtime that best fits (e.g., parser in Go for speed).
- **Cons:** Violates the **First Commandment of Distributed Systems** (KB §7.4): *"Don't distribute."* Introduces network latency between parser and calculator, distributed transaction failure modes across `LogIngestion` and `ProductionShift` persistence, and operational overhead disproportionate for a self-hosted single-plant tool. The baseline ships under `docker-compose up`. Microservices would also require an *Outbox* pattern (KB §17.2) to keep ingestion and persistence atomic — a pattern that is explicitly listed in the roadmap (T3), not the baseline.
- **Reason rejected:** The tool solves one plant's log analysis. It does not have a scaling axis that justifies distribution.

### 4. Pure Hexagonal without Clean/Onion layering

- **Description:** Organize code as `Ports/` and `Adapters/` only; no explicit Domain/Application division.
- **Pros:** Strong isolation at boundaries; symmetric mental model.
- **Cons:** Hides the critical distinction between **domain policy** (`OeeCalculatorEngine`, `ProductionTotals`) and **use-case orchestration** (`ProcessLogFileCommand`). In OEE-Sentinel, that distinction matters — the calculator is the product; the command handler is plumbing. Collapsing them into a single "core" over-interfaces trivial boundaries (which KB §19.1 explicitly warns against: *"over-interfaceing trivial boundaries"*) and obscures where to put new logic during reviews.
- **Reason rejected:** Loses the *use-case vs policy* distinction that Clean/Onion makes explicit.

## Consequences

### Positive

- **Testable in isolation.** `OeeCalculatorEngine` is a pure static method with no dependencies. Its tests (which already exist in `tests/OEESentinel.UnitTests/OeeCalculatorEngineTests.cs`) do not mock anything, do not spin up a database, and do not bind a socket. They are Fast, Independent, Repeatable — the F.I.R.S.T. properties from KB §14.2.
- **Parser is swappable.** `IProductionLogParser` is declared in Application; `CsvProductionLogParser` (CsvHelper) is one adapter. A future `TxtProductionLogParser`, `JsonProductionLogParser`, or `MqttProductionLogParser` is added without touching Domain, Application, or existing tests.
- **Persistence is invisible to the domain.** `ProductionShift` knows nothing of EF Core. It uses `MachineId` (a typed Value Object) and `SourceIngestionId`, not navigation properties. EF Core mapping lives in `Infrastructure` and is configured with `OwnsOne`/`OwnsMany` — the domain never sees a `[Column]` attribute. This directly supports the immutability policy for `ProductionShift` (ADR-0006).
- **Blazor UI cannot leak into the domain.** `WebUI` depends *on* Application; Application does not depend on WebUI. A Blazor component that needs OEE data must call `IGetMachineOeeHandler`, which returns a DTO. The `OeeMetrics` VO is never serialised directly to JSON.
- **Alignment with KB §7.2 and §3.2/§3.3.** The solution structure maps one-to-one onto the KB's recommended partition and its aggregate boundaries.

### Negative / Trade-offs

- **More `.csproj` files and more ceremony.** Four projects plus a test project, with `ProjectReference` discipline maintained by hand. A one-project solution would compile faster. This is accepted per KB §19.1: the boilerplate buys *enforceable* dependency inversion.
- **DTO mapping overhead.** Every query must translate `ProductionShift` → `ShiftOeeDto`. Mapping code is repetitive; `AutoMapper` or `Mapperly` is deliberately **not** introduced in the baseline (extra abstraction, extra training). Hand-written `ToDto()` extension methods live in Application.
- **Risk of over-porting.** It is tempting to introduce `IUnitOfWork`, `IDateTimeProvider`, or a port per repository method. The baseline keeps the port surface minimal: five interfaces total. New ports require an ADR amendment — this is a conscious guard against KB §19.1's *"over-interfaceing trivial boundaries"*.
- **Team discipline requirement.** A single `using Microsoft.EntityFrameworkCore;` in `Domain/Machine.cs` breaks the architecture silently. This is mitigated by code review and by a CI step (planned under roadmap T6) that greps Domain/Application for forbidden namespaces.

## References

- KB §1.1 (4 fases), §3.2 (DDD tactical), §3.3 (Aggregates: ACID vs BASE), §7.1 (Layering vs Tiers), §7.2 (Clean/Onion, Ports & Adapters, Dependency Rule), §7.4 (First Commandment of Distributed Systems), §19.1 (ADR methodology).
- `docs/sad/01-problem-statement.md` §3 (Architectural Objectives).
- `docs/sad/02-context.md` §4 (Maintainability, Testability).
- `docs/domain/domain-model.md` §2–§5.
- Solution layout: `OEESentinel.slnx`, `src/OEESentinel.{Domain,Application,Infrastructure,WebUI}/`, `tests/OEESentinel.UnitTests/`.