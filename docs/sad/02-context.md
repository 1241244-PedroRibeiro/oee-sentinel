# Operational Context & Business Environment: OEE-Sentinel

## 1. Project Context & Industrial Domain Alignment

This document establishes the operational context for **OEE-Sentinel**, an industrial telemetry and OEE (Overall Equipment Effectiveness) analysis engine built for manufacturing plants, assembly lines, and legacy workshops where machines emit raw log files (CSV, TXT, proprietary dumps) but where no integrated MES/ERP is affordable, deployable, or desired.

OEE-Sentinel solves a **specific and under-served** slice of the Industry 4.0 problem: the *post-hoc analytical* pipeline. Real-time SCADA and predictive alerting are addressed by sibling systems (e.g., FlowGuard). OEE-Sentinel consumes the raw artefacts that those systems do not — the humble log files — and turns them into the three pillars that every production manager is judged on: **Availability**, **Performance**, and **Quality**.

The engine is intentionally **self-hostable**, **file-first**, and **database-transparent**: a plant engineer can drop a CSV, get an OEE figure, drill into the Pareto of downtime reasons, and export it — all without a cloud dependency.

---

## 2. Real-World Industrial Benchmark

To avoid building a demo-grade abstraction, OEE-Sentinel is aligned with the operational realities observed in **mid-size discrete manufacturing** and **legacy assembly lines** (the segment with the highest ratio of "has machines" to "has no integrated telemetry platform"):

### Benchmark Environment Profile

- **Sector focus:** Discrete manufacturing, small/medium assembly cells, packaging lines, legacy automotive suppliers.
- **Data reality:**
  - Machines output a text/CSV line per event (`timestamp, machine_code, quantity, error_code`).
  - Files land in shared network folders, named by shift and date.
  - Error codes are **vendor-specific** and must be mapped to a plant-wide downtime catalogue.
  - There is no central clock synchronization; timestamps may drift by seconds.
  - Volume: typically 5k–50k lines per shift per machine; peaks to 200k on high-speed lines.
- **Organizational reality:**
  - The plant manager calculates OEE **manually** in Excel, monthly.
  - Micro-stops (< 5 min) are invisible — operators do not record them.
  - Scrap is tracked in a separate spreadsheet from downtime.
  - No one trusts the number on the wall because no one can trace it back to the source.

### What OEE-Sentinel Changes

| Before | After |
|---|---|
| Excel, monthly | Streaming ingestion, per shift |
| OEE is a single number | A + P + Q, each with source totals |
| Micro-stops invisible | `Duration` totals from log gaps |
| Scrap in another file | `RejectedParts` in the same `ProductionTotals` |
| Untraceable | Every `ProductionShift` linked to its `LogIngestion` |

---

## 3. Stakeholder Ecosystem & User Personas

| Role | Portuguese label | Responsibilities |
|---|---|---|
| **`OPERATOR`** | Operador de Linha | Upload shift log files, view OEE dashboard for own machine, drill into downtime Pareto. Cannot edit machine configuration. |
| **`ENGINEER`** | Engenheiro de Produção | Configure `Machine` (ideal cycle time, planned shift template), maintain `DowntimeReason` catalogue, review trends across machines and shifts. |
| **`ADMINISTRATOR`** | Administrador de Sistema | Provision users, assign roles, manage backups, monitor ingestion health (`LogIngestion` list). |
| **External integrators** | — | MES/ERP systems consuming the REST API for shift-level OEE; future Prometheus scrapers. |

> **Note:** The baseline implementation ships with the three personas as *conceptual* roles, but authentication/RBAC is a **Roadmap Task (T2)**. Baseline access is open on the local Blazor dashboard (Docker-hosted, assumed trusted network). ADR-0002 will document this decision.

---

## 4. Operational Objectives & Quality Drivers (FURPS+ aligned)

| Attribute | Driver | Consequence for Architecture |
|---|---|---|
| **Reliability** | OEE must be reproducible across runs and environments | `OeeCalculatorEngine` is a pure domain service; `ProductionShift` is immutable after persist; raw events kept for re-analysis |
| **Performance** | 200k-line CSV must parse in bounded memory | `CsvHelper` + `IAsyncEnumerable<ProductionEvent>`; never `File.ReadAllLines`; batched bulk insert |
| **Maintainability** | Downtime catalogues and error-code mappings change per client | `DowntimeReason` as reference aggregate, not enum; parser is a *port* (`IProductionLogParser`) with CSV as one adapter |
| **Testability** | OEE math is the core of the product | Domain layer has zero external dependencies; xUnit + FluentAssertions cover clamping, zero-division, and invariant violations |
| **Interoperability** | MES systems consume metrics | Versioned REST API (`/api/v1/...`), OpenAPI documented (future T8) |
| **Usability** | Plant managers must trust the number | Traceability from `OeeMetrics` → `ProductionShift` → `LogIngestion` → raw file |
| **Supportability** | Self-hosted in factories | Docker Compose for PostgreSQL; single `dotnet run` on `WebUI`; no external services required |