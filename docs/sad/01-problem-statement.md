# Problem Statement: OEE-Sentinel — Industrial Production Log Analysis & OEE Engine

## 1. Executive Summary & Business Context

OEE-Sentinel is a robust, high-performance industrial telemetry and operational-efficiency engine engineered specifically for manufacturing plants, assembly lines, and legacy workshop environments where machines generate raw production logs (CSV, TXT, proprietary dumps) that are typically forgotten on shared network folders. In most factories, plant managers still compute **OEE (Overall Equipment Effectiveness)** manually in spreadsheets at the end of the month, closing the door on any real-time reaction to mechanical failures, micro-stops, and quality losses. By the time a figure is on a dashboard, the shift is over and the loss is booked.

The core business challenge addressed by OEE-Sentinel is to replace manual, retrospective spreadsheet analysis with an automated, streaming, auditable pipeline that:

1. Ingests high-volume production logs without loading them entirely into RAM (streaming `IAsyncEnumerable<T>`).
2. Normalizes the raw events into a machine-agnostic domain model.
3. Computes the three OEE pillars — **Availability**, **Performance**, **Quality** — and the consolidated OEE, in a pure, unit-testable domain service.
4. Categorizes downtime by reason codes and surfaces Pareto analysis of the biggest losses.
5. Exposes REST endpoints and a Blazor Server dashboard for operators, engineers, and managers.

OEE-Sentinel is deliberately **not** a real-time SCADA system (that role is fulfilled by sibling projects such as FlowGuard). Its job is the *post-hoc analytical* half of the industrial data problem: legacy file dumps → trustworthy efficiency metrics.

---

## 2. Core Operational Problems to Solve

### 2.1. Resilient Ingestion of Legacy Log Files

Industrial machines write to file, not to a message broker. Common realities:

- Logs grow to tens of thousands of lines per shift.
- Files are often malformed (missing timestamps, mixed locales for decimals, trailing whitespace, unexpected column order).
- Files accumulate in network folders with no schema versioning.

OEE-Sentinel must consume these files **in stream**, reject individual malformed lines without aborting the whole batch, and record per-file diagnostics (`LogIngestion` aggregate) so a plant engineer can see exactly which file failed and why.

### 2.2. Decoupling Raw Logs from Machine Context

A production event in a raw file is just `timestamp, machine_code, quantity, error_code`. It only acquires meaning when cross-referenced with:

- The **machine's ideal cycle time** (in seconds per unit) — the denominator of Performance.
- The **planned shift window** — the denominator of Availability.
- The **downtime reason catalogue** — how error codes map to *planned* vs *unplanned* stops.
- The **scrap markers** — which error codes mean "part rejected" vs "machine stopped".

OEE-Sentinel keeps this contextual knowledge as **reference data** (`Machine`, `DowntimeReason`) and **never bakes it into the log parser**. The parser produces domain events; the `OeeCalculatorEngine` consumes them together with the machine context.

### 2.3. Rigorous OEE Mathematics with Explicit Safeguards

OEE formulas are simple but unforgiving of edge cases:

- Division by zero when `PlannedProductionTime = 0` or `TotalParts = 0`.
- Availability > 1 when downtime is accidentally *negative* (data corruption).
- Performance > 1 when a machine reports more parts than physically possible in the shift window.
- Floating-point rounding on percentage output.

The `OeeMetrics` value object **clamps all values to [0,1]**, rejects negative inputs at construction, and documents (in ADR) that any "impossible" value indicates upstream corruption that must be fixed, not silently hidden. This is a **domain invariant**, not a presentation concern.

### 2.4. Auditability of Every Analytical Result

A production manager cannot act on a number they cannot explain. Therefore:

- Every computed `ProductionShift` is stored with its **source** (the `LogIngestion` that produced it).
- Raw events remain available for re-analysis (append-only table `production_events`).
- The `OeeMetrics` computed at ingestion time is stored next to the raw totals, so the report can be reproduced even if the calculator is later revised.
- Corrections create a **new** `ProductionShift` row linked to the superseded one; historical rows are never mutated.

### 2.5. Extensible Roadmap for Future Integration

OEE-Sentinel is architected as an extensible engine that will grow over **20+ hours of planned AI-assisted development**:

- PDF report export (QuestPDF) with Pareto breakdowns.
- Real-time folder watcher (drop-zone ingestion via `FileSystemWatcher` + BackgroundService).
- JWT authentication and RBAC (`OPERATOR`, `ENGINEER`, `ADMINISTRATOR`).
- Prometheus/OpenMetrics export for enterprise monitoring.
- Comparative trend dashboards (week-over-week OEE by machine and by sector).

---

## 3. High-Level Architectural Objectives

1. **Clean / Onion Architecture** — `Domain` (pure), `Application` (use cases), `Infrastructure` (EF Core, CsvHelper, parsers), `WebUI` (Blazor Server + REST controllers).
2. **Domain-first math** — `OeeCalculatorEngine` is a *pure* domain service with zero external dependencies; testable with xUnit + FluentAssertions, no mocks required.
3. **Streaming ingestion** — `CsvHelper` with `IAsyncEnumerable<ProductionEvent>`; no `ReadAllLines`.
4. **Bulk persistence** — `EFCore.BulkExtensions.PostgreSql` for the `production_events` table; batched inserts of thousands of rows per shift.
5. **REST API + Blazor UI** — dual front-end strategies: interactive dashboard for humans, versioned JSON API for MES integration.
6. **Test coverage** — domain invariants (OEE math edge cases, `ProductionTotals` consistency), application use cases, and integration of the parser against `samples/*.csv`.

---

## 4. Current Implementation Scope & Future 20-Hour Roadmap

### Implemented Baseline (target at submission):

- **Domain core**: `OeeMetrics`, `Machine`, `ProductionShift`, `OeeCalculatorEngine`, `ProductionTotals`, `DowntimeReason`, with unit tests covering happy path + edge cases (zero produced, negative downtime rejected, clamping).
- **Application**: `ProcessLogFileCommand` orchestrating upload → parse → compute → persist.
- **Infrastructure**: `CsvLogParser` (streaming), `OEESentinelDbContext` (EF Core / PostgreSQL), initial migration.
- **WebUI**: upload page, machine dashboard with OEE gauges (`Chart.js`), REST endpoints `POST /api/logs/upload` and `GET /api/machines/{code}/oee`.
- **DevOps**: `docker-compose.yml` (PostgreSQL), README, LICENSE (MIT), `.env.example`, `samples/sample_shift.csv`.

### Planned Future Work (20+ hours Roadmap):

| Task | Description | Est. |
|---|---|---|
| **T1** | PDF report export (QuestPDF) — per-shift and per-machine, with Pareto chart | 3h |
| **T2** | JWT auth + RBAC (`OPERATOR`/`ENGINEER`/`ADMINISTRATOR`) | 4h |
| **T3** | Background `FileSystemWatcher` for automated drop-folder ingestion | 2.5h |
| **T4** | Pareto analysis dashboard (top-5 downtime reasons per machine/sector) | 3h |
| **T5** | OpenMetrics `/metrics` endpoint (Prometheus-compatible) | 2h |
| **T6** | Expanded test suite (parser fuzzing, boundary cases, integration) | 3h |
| **T7** | `BenchmarkDotNet` performance baseline for parser + calculator | 2h |
| **T8** | SAD, C4 Context+Container diagrams, 4 ADRs | 2.5h |