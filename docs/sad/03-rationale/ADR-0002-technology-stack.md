# ADR-0002: Runtime, Language, and Core Platform Stack

- **Status:** Accepted
- **Date:** 2026-11-04
- **Decision Makers:** OEE-Sentinel Development Team

## Context

OEE-Sentinel is a self-hosted industrial analytics engine that must:

1. **Parse very large CSV/TXT files** (5k–200k lines per shift per machine, per `docs/sad/02-context.md` §2) in **bounded memory**, ideally with sub-second per-100k-rows throughput.
2. **Compute OEE math** (A, P, Q, OEE) deterministically with strict clamping and invariant enforcement.
3. **Host a dashboard** that renders gauges and Pareto charts for plant operators, sharing **models** between backend and UI without duplicating type definitions or building a JS bridge.
4. **Expose a versioned REST API** for MES/ERP integration.
5. **Persist** aggregated shifts and raw event facts into PostgreSQL with bulk insertion.
6. **Run** in a factory's local network with a single `docker-compose up`.

The physical environment — a factory server with a modest footprint, no cloud dependency, no container orchestrator — rules out serverless, heavy microservice mesh, and multi-language polyglot backends. The team is a single developer working with an AI coding assistant, and BYOP submission (`(Ext) BYOP - Project submission instructions.pdf` p.2) requires "a software project you personally own … ideally with signs of use". A stack must be **productive for one developer** and **verifiable by an external reviewer**.

## Decision

We adopt the **C# / .NET 10 / ASP.NET Core** platform with **Blazor Server** for the UI (see ADR-0004), **PostgreSQL 16** as the persistence engine (ADR-0003), and **`CsvHelper` + `EFCore.BulkExtensions` + `xUnit` + `FluentAssertions`** as the library baseline.

Concretely, and per project:

| Layer | Project | Reference assemblies | Notable packages |
|---|---|---|---|
| Domain | `OEESentinel.Domain` | `net10.0` — pure class library | *(none)* |
| Application | `OEESentinel.Application` | `net10.0` — pure class library | *(none; DI interfaces via constructor injection only)* |
| Infrastructure | `OEESentinel.Infrastructure` | `net10.0` | `Microsoft.EntityFrameworkCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `EFCore.BulkExtensions.PostgreSql`, `CsvHelper` |
| WebUI | `OEESentinel.WebUI` | `net10.0` — ASP.NET Core | `Microsoft.EntityFrameworkCore.Design` (for migrations) |
| Tests | `OEESentinel.UnitTests` | `net10.0` | `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `FluentAssertions` |

**Solution file format:** `.slnx` (XML solution), supported natively by the .NET 10 SDK. We deliberately do **not** maintain a parallel `.sln`; the trade-off (see below) is that older .NET 8 SDKs cannot open the solution — a constraint acceptable because the project targets .NET 10 exclusively and CI runs on .NET 10.

**Test project base:** `xUnit` (v2, not v3) for ecosystem maturity with `FluentAssertions`. No test-specific mocking library in the baseline — the domain is pure and does not need mocks; `Infrastructure` integration tests are deferred to roadmap T6.

## Considered Alternatives

### 1. Node.js + TypeScript (NestJS) — the LaPrizza stack

- **Description:** Reuse the stack of the sibling LaPrizza project — Node.js LTS, NestJS, `pg`, TypeORM.
- **Pros:** One language across the whole team's ecosystem; massive ecosystem of parsing libraries; asynchronous I/O is idiomatic; NestJS provides strong modular boundaries (see ADR-001 of LaPrizza).
- **Cons:** A **single-threaded event loop** is a structural mismatch with the OEE-Sentinel use case. Parsing 200k CSV rows is CPU-bound, not I/O-bound; the Node.js event loop would freeze on `for await (const row of parser.read())` unless the parser is offloaded to a worker thread. `fs.createReadStream` reduces file I/O blocking but does not touch the CPU cost of row parsing. Additionally, the .NET ecosystem around **Blazor** provides model sharing with the UI that NestJS+React cannot match without either a codegen step or duplicate TS/C# type definitions. There is no equivalent of `IAsyncEnumerable<T>` returning C# `record` instances directly bound into a Razor component.
- **Reason rejected:** The runtime model is misaligned with the dominant workload (CPU-bound parsing), and the UI/backend model-sharing benefit of Blazor is lost.

### 2. Python + FastAPI + pandas

- **Description:** Python 3.12 + FastAPI, with `pandas` for the OEE math and `polars` for large-file parsing.
- **Pros:** Excellent data handling libraries; extremely fast prototyping; `polars` is genuinely faster than CsvHelper on wide CSVs.
- **Cons:** Deployment in a factory is painful (`uv`/`poetry`/`pip` + native wheels). Startup time for FastAPI under systemd or Docker is fast, but the GIL prevents true parallel parsing without multiprocessing, which reintroduces complexity. Type safety is weaker, and the OEE math would drift into `pandas` semantics (NaN propagation, dtype coercion) — making the "pure, unit-testable math" guarantee of ADR-0001 harder to defend. xUnit-style structured tests are available in Python but FluentAssertions' readable assertions have no close equivalent.
- **Reason rejected:** Weaker type guarantees; harder deployment; math domain would live inside pandas, not a value object.

### 3. Java 21 + Spring Boot + Thymeleaf

- **Description:** Spring Boot 3, Thymeleaf templates, Spring Data JPA, PostgreSQL.
- **Pros:** Battle-tested enterprise stack; extremely mature ecosystem; excellent tooling (JFR, JMC, Jaeger).
- **Cons:** Higher boilerplate (annotations, `@SpringBootApplication`, DTO mapping). Thymeleaf lacks the model-sharing and interactive re-rendering of Blazor. Java 21 is fast but JVM warm-up under a docker-compose `up` adds ~10s to first request. The `.slnx`+`dotnet watch` loop is faster than `mvn spring-boot:run`. Most relevantly, the model-sharing story (C# `record` in Domain ↔ Blazor component) has no equivalent in Thymeleaf without a DTO codegen.
- **Reason rejected:** More ceremony, weaker UI model-sharing, slower iteration cycle for a solo developer.

### 4. F# + ASP.NET Core + Giraffe

- **Description:** Same runtime as C#, but with F#'s type system and pipeline operators.
- **Pros:** The OEE formulas are naturally expressible as pure functions. Discriminated unions would model `IngestionStatus` and `DowntimeCategory` elegantly. Immutability is the default.
- **Cons:** The ecosystem for `CsvHelper`-class libraries, `EFCore.BulkExtensions`, and xUnit is *available* but the idiomatic patterns diverge from mainstream .NET. Reviewers of the BYOP submission (`(Ext) BYOP - Project submission instructions.pdf` p.3: "reviewers read it to confirm what you tell us") are more likely to recognise and trust a C# codebase. F# as a first language for an industrial self-hosted tool is a niche choice that adds friction without proportional benefit — the domain is small enough that C# with `record` types covers 95% of the F# benefit.
- **Reason rejected:** Practical familiarity and reviewer-recognisability outweigh F#'s expressiveness on a small domain.

### 5. Go + Gin + sqlc + templ

- **Description:** Go 1.23, `gin-gonic` for HTTP, `sqlc` for type-safe SQL, `templ` for HTML templating.
- **Pros:** Excellent runtime performance; single static binary; strong concurrency primitives for parallel parsing.
- **Cons:** No equivalent of `IAsyncEnumerable<T>` for streaming parse with the ergonomics of C#. No rich value-object story — Go lacks operator overloading and value types, so `Percentage` as a distinct type with clamping would be `type Percentage float64` and rely on convention. `sqlc` is excellent but the ORM ecosystem (`EFCore.BulkExtensions`-class) is less mature. `templ` templates are type-safe but do not share C# models with the engine. The developer's fluency is C#/.NET, not Go.
- **Reason rejected:** Weak value-object support; no model-sharing with the parser.

### 6. `.slnx` vs classic `.sln`

- **Description:** Two solution formats.
- **Pros of `.slnx`:** XML, merge-friendly, smaller; native support since .NET SDK 9.0.200+; default in `dotnet new` on .NET 10.
- **Pros of `.sln`:** Universally supported by `dotnet build`, older Visual Studio versions, and CI templates that predate .NET 9.
- **Cons of `.slnx`:** Not supported by .NET 8 SDK; older tooling may not parse it.
- **Cons of `.sln`:** Verbose, merge-prone, hard to read.
- **Reason chosen `.slnx`:** Project targets .NET 10 exclusively. The trade-off is documented in `README.md` (target `.NET 10 SDK` as a prerequisite). CI pins `dotnet-version: '10.0.x'`. The BYOP review (`(Ext) BYOP - Project submission instructions.pdf` p.3: "A README that says … what it's built with and how to run it") is satisfied by the README's explicit `.NET 10 SDK` prerequisite — the reviewer knows immediately.

## Consequences

### Positive

- **CPU-bound parsing runs on the .NET thread pool,** not the event loop. `Parallel.ForEachAsync` over batches, or `IAsyncEnumerable<ProductionEvent>` with `[EnumeratorCancellation]` and an awaited `await foreach`, both map cleanly onto the C# async state machine.
- **Streaming parsing is idiomatic C#.** `CsvHelper`'s `GetRecordsAsync<T>()` returns `IAsyncEnumerable<T>`; a `foreach await` loop consumes it without buffering. Memory stays bounded regardless of file size.
- **Model-sharing between backend and UI.** `OeeMetricsDto` defined in `Application` is consumed by both `GET /api/v1/machines/{code}/oee` (serialised to JSON) and the Blazor `MachineOeePanel.razor` component (bound directly). One definition, one source of truth, zero bridging code.
- **Bulk insert via `EFCore.BulkExtensions.PostgreSql`** is a first-class supported operation. Inserting 200k `ProductionEvent` rows per shift is a `BulkInsertAsync` call; there is no equivalent ergonomic path in most other ORMs.
- **Single static `.slnx` solution** opens with one command; no `docker-compose exec` gymnastics to run tests.
- **`xUnit` + `FluentAssertions`** produce readable, reviewable math assertions (`result.Oee.Should().BeApproximately(0.75, 0.001)`) that document the OEE domain itself.

### Negative / Trade-offs

- **`.NET 10` is recent.** As of submission, some tooling (notably older Rider / VS 2022 without the latest updates) may not recognise `.slnx`. The README declares `.NET 10 SDK` as a prerequisite; reviewers who lack it can install it in under a minute.
- **`EFCore.BulkExtensions`** is a third-party library (not Microsoft-owned). It is mature but a supply-chain risk. Mitigated by pinning the version in `Directory.Packages.props` (to be introduced under roadmap T7).
- **`CsvHelper`** is also third-party. The version is pinned in the `.csproj`. The parser is behind `IProductionLogParser` (ADR-0001), so a future replacement is a one-file change in `Infrastructure`.
- **No JS bridge.** If a future feature needs a JS-rich component (e.g., a 3D Pareto visualisation), a JS interop layer (`IJSRuntime`) must be introduced. The baseline UI is Chart.js only (gauge + bar), which has a clean JS-interop story.
- **No mocking library in the baseline.** When Application handlers need to be tested in isolation (e.g., `ProcessLogFileCommand` with a fake `IProductionLogParser`), a `FakeParser` implementation is hand-written. `Moq` or `NSubstitute` is deferred until a real need emerges (roadmap T6).

## References

- KB §7.2 (Clean Architecture), §9.2 (layered structure, but for NestJS — used as a contrast), §14 (testing), §22.1 (CI, tooling).
- `docs/sad/01-problem-statement.md` §3, §4.
- `docs/sad/02-context.md` §2 (benchmark), §4 (quality drivers).
- `(Ext) BYOP - Project submission instructions.pdf` pp. 2–3 ("built with", "README requirements").
- `.slnx`, `src/OEESentinel.*/*.csproj`, `tests/OEESentinel.UnitTests/*.csproj`.
- `README.md` ("What it's built with", "Prerequisites").