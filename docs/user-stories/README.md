# User Stories — OEE-Sentinel

This directory indexes the project's **user stories**, their scope (baseline
vs. planned), and the artifacts (ADR, spec, handler) they map to. User
stories follow the format from KB §2.1 and §18.1: a short "As a `<role>`, I
want `<capability>`, so that `<benefit>`" card, plus acceptance criteria
when the story is non-trivial.

## 1. Conventions

- **Identifier:** `<CAPABILITY>-<NN>` (e.g., `LOG-01`, `OEE-02`, `AUTH-01`).
  Capability prefixes are listed in §2.
- **Status:** `✅ Baseline` (implemented before submission), `🚧 Planned`
  (scheduled under a roadmap task), `❌ Rejected` (documented and excluded
  from scope).
- **Links:** each story links to its handler in `Application/Handlers/`,
  its ADR, and (when applicable) its `docs/sad/05-key-scenarios.md` scenario.
  The links use repository paths and are checked during review.
- **Ownership of requirements:** the story *is* the requirement. If a
  behavioural detail needs to be recorded, it is added as an *Acceptance
  Criteria* bullet under the story, not in a separate document.

## 2. Capability prefixes

| Prefix | Capability | Description |
|---|---|---|
| `LOG` | Log ingestion | Upload, parse, persist shift logs |
| `OEE` | OEE computation & views | OEE math, dashboard, Pareto, trends |
| `MACH` | Machine catalogue | Reference data for machines and downtime reasons |
| `AUTH` | Authentication & RBAC | JWT, role authorization (planned T2) |
| `REPORT` | Reporting | PDF export of shift/machine reports (planned T1) |
| `AUTO` | Automation | Folder watcher ingestion (planned T3) |
| `OBS` | Observability | Prometheus metrics, health (partial baseline) |
| `CORR` | Correction workflow | Shift supersession and correction UI (planned T4) |

## 3. Baseline user stories

These stories ship in `v0.1.0-baseline`. Acceptance criteria are in
`ROADMAP.md` P0-1..P0-9.

### LOG — Log ingestion

#### LOG-01 — Upload a production log ✅ Baseline

> As a **Plant Operator**, I want to **upload a CSV/TXT shift log** so that **the system computes and persists the shift OEE without manual calculation.**

- **Handler:** `ProcessLogFileHandler`
- **UI:** `Upload.razor`
- **REST:** `POST /api/v1/logs`
- **ADR:** ADR-0005 (streaming parser), ADR-0006 (immutable fact)
- **Scenario:** `05-key-scenarios.md` §2 (streaming ingestion of a 200k-line shift)
- **Acceptance criteria:**
  - A file with a valid header (or no header) and the canonical row format is accepted.
  - Malformed lines increment `RowsRejected`; the whole batch is not aborted.
  - Duplicate window → `409`; planned time zero → `422`; unknown machine → `404`.
  - The `ProductionShift` returned is immutable and linked to its source ingestion.

#### LOG-02 — Poll ingestion status ✅ Baseline

> As an **external MES**, I want to **retrieve the status of a log ingestion** so that **I can react to failures programmatically.**

- **Handler:** `ListRecentIngestionsHandler` (single read)
- **UI:** `Ingestions.razor` shows the same data as a table
- **REST:** `GET /api/v1/logs/{ingestionId}`
- **Acceptance criteria:**
  - Returns `PENDING | PROCESSING | COMPLETED | FAILED` with row counts.
  - `rejectedSample` includes up to 100 rejected rows with line numbers and reasons.
  - `productionShiftId` is populated only when `status == COMPLETED`.

### OEE — OEE computation & views

#### OEE-01 — View single-shift OEE ✅ Baseline

> As a **Plant Operator** or **Manager**, I want to **view the A/P/Q/OEE pillars of a single shift** so that **I can investigate a specific production outcome.**

- **Handler:** `GetMachineShiftHandler`
- **UI:** `ShiftDetail.razor`
- **REST:** `GET /api/v1/machines/{code}/shifts` (paginated) / single by id
- **Acceptance criteria:**
  - The four pillars are rendered as `OeeGauge` components with the `Percentage` value scaled to `0–100`.
  - The downtime breakdown is listed with reason code, description, and duration.
  - A superseded shift shows a banner with a link to the superseding shift (T4 will add the UI; the DTO field exists).

#### OEE-02 — View aggregated OEE trend ✅ Baseline

> As a **Manager**, I want to **view OEE aggregated across a window** so that **I can assess trend without skew from a single outlier shift.**

- **Handler:** `GetMachineOeeHandler`
- **UI:** `MachineDetail.razor` (with `Trend.razor` as a dedicated view planned T4)
- **REST:** `GET /api/v1/machines/{code}/oee`
- **Acceptance criteria:**
  - Aggregation is **from totals** (`Σ Operating / Σ Planned`), never by averaging shift-level percentages.
  - Window is validated: `to > from`, span ≤ 90 days.
  - Empty window → `422` with a stable reason code.

#### OEE-03 — View downtime Pareto ✅ Baseline

> As a **Production Engineer**, I want to **see the top downtime reasons by duration** so that **I can prioritise the highest-loss causes.**

- **Handler:** `GetDowntimeParetoHandler`
- **UI:** `Pareto.razor`
- **REST:** `GET /api/v1/machines/{code}/pareto`
- **Scenario:** `05-key-scenarios.md` §4
- **Acceptance criteria:**
  - Top-20 entries by duration, sorted descending; ties broken by `reasonCode` ascending.
  - Percentages sum to `1.0` (or `0.0` for an empty window).
  - Window guard as in OEE-02.

### MACH — Machine catalogue (baseline = seeded only)

#### MACH-01 — Access seeded machine and downtime reason reference data ✅ Baseline

> As a **Plant Operator**, I want to **see the list of machines and downtime reasons** so that **I can pick the correct machine when uploading and interpret the breakdown.**

- **Handler:** `IMachineRepository.ListActiveAsync`, `IDowntimeReasonRepository.ListActiveAsync`
- **UI:** dropdown selectors in `Upload.razor` and `Home.razor`; `GET /api/v1/machines` for the list.
- **Acceptance criteria:**
  - Seed contains at least 3 machines and 6 downtime reasons (per `08-database-structure.md` §7).
  - `GET /api/v1/machines` returns paginated, searchable list.
- **Not included:** create/edit/delete of machines or downtime reasons — see MACH-02 (planned T2).

### OBS — Observability

#### OBS-01 — Health probe ✅ Baseline

> As an **operator or monitoring system**, I want to **probe the system's health** so that **I can detect database unavailability before users are affected.**

- **Handler:** inline `WebUI` handler (`SELECT 1` with 2s timeout)
- **REST:** `GET /api/v1/health`
- **Acceptance criteria:**
  - `200 OK` with `{ status: "ok", database: "connected", … }` when healthy.
  - `503 Service Unavailable` when the database is unreachable.
  - Uptime reported in seconds.

## 4. Planned user stories (roadmap T1–T8)

These stories are **explicitly scheduled**, not yet implemented. Each links
to a roadmap task in `ROADMAP.md`. Their ADRs will be written when they
begin.

### AUTH — Authentication & RBAC (Roadmap T2)

#### AUTH-01 — Authenticate via JWT 🚧 T2

> As a **registered user**, I want to **exchange credentials for a JWT** so that **I can access protected endpoints without re-sending my password.**

- **Scope:** Argon2id password hashing, 8h shift expiration, role claims (`OPERATOR`, `ENGINEER`, `ADMINISTRATOR`).
- **Design constraint:** the `User` aggregate and `users` table already exist in baseline; the migration adds no schema change (ADR-0002 §Context, ADR-0006 §2.4).
- **Cross-reference:** `05-key-scenarios.md` §6 (planned scenario).

#### AUTH-02 — Role-based access control 🚧 T2

> As an **administrator**, I want to **assign roles to users** so that **only engineers can configure machines and only operators can upload logs.**

- **Scope:** `[Authorize(Roles="…")]` attributes on endpoints and pages; Blazor `AuthenticationStateProvider` reading the JWT from an HttpOnly cookie or the circuit state.
- **Cross-reference:** `05-key-scenarios.md` §6.

### MACH — Machine management (Roadmap T2, requires AUTH-02)

#### MACH-02 — Manage machines and downtime reasons 🚧 T2

> As an **engineer**, I want to **create, update, and deactivate machines and downtime reasons** so that **the catalogue evolves with the plant without redeployment.**

- **Scope:** `POST/PUT/DELETE` endpoints (soft deactivation for `Machine`; `DowntimeReason` unchanged when referenced — `409`).
- **Cross-reference:** ADR-0003 (`ON DELETE RESTRICT`), `docs/sad/04-principles` §8.

### REPORT — PDF export (Roadmap T1)

#### REPORT-01 — Export shift and machine reports as PDF 🚧 T1

> As a **manager**, I want to **export a shift or machine window as a PDF** so that **I can attach it to a review or archive it outside the tool.**

- **Scope:** `GET /api/v1/machines/{code}/shifts/{id}.pdf` and `GET /api/v1/machines/{code}/report.pdf`. QuestPDF library. Include A/P/Q/OEE, downtime breakdown, Pareto chart.
- **Cross-reference:** `ROADMAP.md` T1.

### AUTO — Automation (Roadmap T3)

#### AUTO-01 — Auto-ingest from a drop folder 🚧 T3

> As an **operator**, I want to **place a log file in a configured folder** so that **it is ingested automatically without manual upload.**

- **Scope:** `FileSystemWatcher` in a `BackgroundService` that calls `IProcessLogFileHandler` with the configured `MachineCode` and parsed window. `LogIngestion` gains a `triggered_by = WATCHER` column (small migration).
- **Cross-reference:** `ROADMAP.md` T3; `05-key-scenarios.md` §2 (async path reserved in `POST /api/v1/logs`).

### CORR — Correction workflow (Roadmap T4)

#### CORR-01 — Correct a shift via supersession 🚧 T4

> As an **engineer**, I want to **correct a shift's downtime mapping without destroying the original fact** so that **historical reports remain auditable.**

- **Scope:** `POST /api/v1/logs/{id}/correct` (new ingestion that creates a new `ProductionShift` and sets `superseded_by_id` on the old one). The `superseded_by_id` column and the partial unique index are already in the baseline schema (ADR-0006 §Decision.3); only the UI and endpoint are new.
- **Cross-reference:** `05-key-scenarios.md` §4 (scenario 3); ADR-0006.

### OBS — Prometheus (Roadmap T5)

#### OBS-02 — Expose Prometheus / OpenMetrics metrics 🚧 T5

> As a **monitoring system**, I want to **scrape OEE-Sentinel metrics** so that **plant-wide dashboards can include ingestion and OEE health.**

- **Scope:** `prometheus-net` middleware in `WebUI`; `GET /metrics` exposing counters `oee_ingestion_total`, `oee_parse_errors_total` and gauges `oee_shift_global`, `oee_active_machines`.
- **Cross-reference:** `ROADMAP.md` T5.

### OEE — Additional reads (Roadmap T4)

#### OEE-04 — Pareto dedicated read view 🚧 T4

> As a **Plant Manager**, I want to **compare Pareto breakdowns across machines** so that **I can identify the plant-wide top losses.**

- **Scope:** SQL view `v_machine_downtime_pareto` (`07-relational-mapping` §5.2) + a new `GetPlantParetoHandler`. Only justified once the in-memory aggregation (OEE-03) hits a performance threshold, benchmarked under T7.
- **Cross-reference:** ADR-0003 (JSONB breakdown decision), `05-key-scenarios.md` §4.

## 5. Summary matrix

| ID | Capability | Title | Status | Handler | Roadmap |
|---|---|---|---|---|---|
| LOG-01 | Log ingestion | Upload a production log | ✅ | `ProcessLogFileHandler` | — |
| LOG-02 | Log ingestion | Poll ingestion status | ✅ | `ListRecentIngestionsHandler` | — |
| OEE-01 | OEE | View single-shift OEE | ✅ | `GetMachineShiftHandler` | — |
| OEE-02 | OEE | View aggregated OEE trend | ✅ | `GetMachineOeeHandler` | — |
| OEE-03 | OEE | View downtime Pareto | ✅ | `GetDowntimeParetoHandler` | — |
| MACH-01 | Machine catalogue | Access seeded reference data | ✅ | `IMachineRepository` | — |
| OBS-01 | Observability | Health probe | ✅ | inline `WebUI` | — |
| AUTH-01 | Authentication | Authenticate via JWT | 🚧 | `AuthHandler` (new) | T2 |
| AUTH-02 | Authentication | Role-based access control | 🚧 | guard pipeline | T2 |
| MACH-02 | Machine catalogue | Manage machines / reasons | 🚧 | `ManageMachineHandler` (new) | T2 |
| REPORT-01 | Reporting | Export PDF reports | 🚧 | `ExportPdfHandler` (new) | T1 |
| AUTO-01 | Automation | Folder-watcher ingestion | 🚧 | `BackgroundService` (new) | T3 |
| CORR-01 | Correction | Correct a shift via supersession | 🚧 | `CorrectShiftHandler` (new) | T4 |
| OBS-02 | Observability | Prometheus `/metrics` | 🚧 | `prometheus-net` middleware | T5 |
| OEE-04 | OEE | Plant-wide Pareto view | 🚧 | `GetPlantParetoHandler` (new) | T4 |

**Baseline total:** 7 stories (LOG-01, LOG-02, OEE-01, OEE-02, OEE-03, MACH-01, OBS-01).
**Planned total:** 8 stories (AUTH-01, AUTH-02, MACH-02, REPORT-01, AUTO-01, CORR-01, OBS-02, OEE-04).

## 6. Traceability

Each story must have, at the time it is *implemented*:

1. **Handler** in `OEESentinel.Application/Handlers/` (or inline `WebUI` for trivial cases like OBS-01).
2. **Port interface** in `OEESentinel.Application/Ports/Inbound/` if the handler is consumed by both Blazor and REST.
3. **DTO** in `OEESentinel.Application/Dtos/`.
4. **UI page** in `Components/Pages/` (baseline).
5. **REST endpoint** in `Program.cs` + **entry** in `docs/api/openapi.yaml`.
6. **Acceptance criteria** verified by tests (`tests/OEESentinel.UnitTests/`) or manual verification (documented in the story's `ROADMAP.md` task).
7. **Reference** from `docs/architecture/use-case-diagram.md` §3 (a use case exercises it) and from `docs/architecture/architecture-summary.md` §3 (functionality summary).

Missing any of the seven is a review blocker.

## 7. Deferred / rejected stories

The following were considered and **explicitly excluded** from the project:

| Story | Reason |
|---|---|
| Real-time MQTT ingestion (in-process) | Sibling system (FlowGuard) owns the real-time pipeline. OEE-Sentinel is *post-hoc* file analytics (see `01-problem-statement.md` §1). |
| Predictive ML anomaly detection | Out of scope. Would require training data that is not available. |
| Mobile native app | Blazor Server is responsive enough for tablet use; a native app adds maintenance cost with no plant-relevant benefit. |
| Multi-tenant SaaS deployment | The tool is single-plant, self-hosted (`02-context.md` §1). |
| Event sourcing for `ProductionShift` | Over-engineered. Supersession (CORR-01) covers the correction need. |
| GraphQL API | REST at Richardson L2 satisfies MES/ERP integration; GraphQL adds complexity without a concrete consumer. |

## 8. References

- KB §2.1 (User Stories), §18.1 (User Story template).
- `docs/sad/01-problem-statement.md`, `02-context.md`, `04-principles-and-guidelines.md`, `05-key-scenarios.md`.
- `docs/domain/domain-model.md`.
- `docs/api/openapi.yaml`.
- `docs/architecture/use-case-diagram.md` (each story maps to a use case).
- `docs/architecture/architecture-summary.md`.
- `ROADMAP.md` (P0-1..P0-9 for baseline; T1–T8 for planned).
- ADR-0001 through ADR-0007.