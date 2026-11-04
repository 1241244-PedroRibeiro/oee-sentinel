# Roadmap — OEE-Sentinel

This roadmap separates **what must ship before submission** (Phase 0 — getting to a mature, active baseline) from **the 20+ hour planned roadmap** (Phases 1–5 — post-selection, AI-assisted sprints).

Each item is scoped to be independently testable and mergeable as a small PR. Estimated effort assumes a 1–2 session cadence with an AI coding assistant.

---

## Phase 0 — Baseline (pre-submission) · ~15h

Getting OEE-Sentinel from skeleton to a runnable, testable baseline with a real domain, a working upload pipeline, and a dashboard. **This is what gets the repo "active and real" (per BYOP guidance).**

| # | Task | Deliverable | Est. |
|---|---|---|---|
| P0-1 | Domain core: `OeeMetrics`, `Percentage`, `Duration`, `ProductionTotals`, `ProductionShift`, `OeeCalculatorEngine` | Fully tested value objects + calculator | 3.0h |
| P0-2 | Domain entities: `Machine`, `DowntimeReason`, `LogIngestion`, `User` + repositories interfaces | ARs + ports | 2.0h |
| P0-3 | Unit tests: happy path + edge cases (zero produced, downtime > planned, clamping) | xUnit + FluentAssertions, ≥ 25 tests | 2.0h |
| P0-4 | `CsvLogParser` (streaming, `IAsyncEnumerable<ProductionEvent>`) + canonical sample | Adapter + `samples/sample_shift.csv` | 2.0h |
| P0-5 | EF Core `DbContext`, value object mappings (`OwnsOne`), initial migration | Schema in PostgreSQL, `production_events` bulk-ready | 2.0h |
| P0-6 | `ProcessLogFileCommand` + handler (parse → compute → persist) | Application layer wired | 1.5h |
| P0-7 | REST API: `POST /api/v1/logs`, `GET /api/v1/machines/{code}/oee` | Controllers + DTOs + OpenAPI annotations | 1.0h |
| P0-8 | Blazor dashboard: upload page + OEE gauges (Chart.js) | UI end-to-end | 1.5h |
| P0-9 | Repo polish: `LICENSE`, `.env.example`, `docker-compose.yml`, `README` final, `ROADMAP`, `docs/` complete | BYOP-ready | (this batch) |

**Exit criterion:** `git clone && docker compose up -d && dotnet ef database update && dotnet run` produces a working app in under 5 minutes on a clean machine.

---

## Phase 1 — Analytics Depth (post-selection) · ~6h

Turn the dashboard from "one number per machine" into an analytical tool.

| # | Task | Deliverable | Est. | Why it matters |
|---|---|---|---|---|
| T1 | **PDF report export** via QuestPDF — per shift and per machine, with Pareto chart | `/api/v1/reports/shift/{id}.pdf` + Blazor download button | 3.0h | Managers need something to attach to a review; PDF is the format factories actually consume. |
| T4 | **Pareto dashboard** — top-5 downtime reasons per machine, per sector, per week | `/dashboard/pareto` + query + chart | 3.0h | The single most actionable view: *"where is the loss coming from?"*. |

---

## Phase 2 — Security & Roles · ~5h

Bring the baseline to a deployable state for a trusted-network environment.

| # | Task | Deliverable | Est. | Why it matters |
|---|---|---|---|---|
| T2 | **JWT auth + RBAC** (`OPERATOR` / `ENGINEER` / `ADMINISTRATOR`) | `/api/v1/auth/login`, password hashing (Argon2id), `[Authorize(Roles=...)]` on controllers, Blazor auth state | 4.0h | Baseline assumes trusted network; a real factory deployment needs role segregation. |
| T2b | **Authorization tests** (unauthorized, forbidden, forbidden-role) | Integration tests via `WebApplicationFactory` | 1.0h | RBAC regressions are silent — must have tests. |

---

## Phase 3 — Automation & Observability · ~4.5h

Shift from "operator uploads a file" to "files arrive and get processed".

| # | Task | Deliverable | Est. | Why it matters |
|---|---|---|---|---|
| T3 | **Drop-folder watcher** — `BackgroundService` with `FileSystemWatcher` polling a configured folder | Ingests new files automatically; status surfaced in `/ingestions` | 2.5h | Manual upload does not scale. Real plants drop files, they do not click buttons. |
| T5 | **Prometheus `/metrics` endpoint** | `prometheus-net` middleware + `oee_shift_global`, `oee_ingestion_total`, `oee_parse_errors_total` gauges | 2.0h | Bridges OEE-Sentinel into existing enterprise monitoring. |

---

## Phase 4 — Quality & Robustness · ~5h

Harden what exists.

| # | Task | Deliverable | Est. | Why it matters |
|---|---|---|---|---|
| T6 | **Expanded test suite** — parser fuzzing (malformed CSV, wrong column count, bad locale), 50+ integration cases | `OEESentinel.IntegrationTests` project | 3.0h | The parser is the highest-risk component; it handles user-supplied input. |
| T7 | **BenchmarkDotNet** baseline for parser + calculator | `OEESentinel.Benchmarks` project, published results in README | 2.0h | "Streaming" claims are worthless without numbers. 200k-line file must parse in bounded memory. |

---

## Phase 5 — Documentation & Governance · ~2.5h

Make the architecture legible to a reviewer (and to future contributors).

| # | Task | Deliverable | Est. | Why it matters |
|---|---|---|---|---|
| T8 | **SAD (Software Architecture Document)** + **C4 Context & Container diagrams** (Mermaid) + **ADR-0003** (immutable `ProductionShift`) + **ADR-0004** (parser as port) | `docs/sad/` | 2.5h | The code tells *what*; the SAD tells *why*. Required by the KB (§19). |

---

## Totals

| Phase | Est. |
|---|---|
| Phase 0 — Baseline (pre-submission) | ~15h |
| Phase 1 — Analytics Depth | ~6h |
| Phase 2 — Security & Roles | ~5h |
| Phase 3 — Automation & Observability | ~4.5h |
| Phase 4 — Quality & Robustness | ~5h |
| Phase 5 — Documentation | ~2.5h |
| **Post-selection roadmap (T1–T8)** | **~23h** |

The **20+ hour figure** cited in `docs/problem-statement.md` §4 corresponds to Phases 1–5 (23h total), with Phase 0 being the pre-submission effort that establishes the "active and real" baseline required by BYOP.

---

## Tracking

- Each task above will become a **GitHub Issue** with a checklist.
- Baseline tasks (P0-1 … P0-9) are tracked in the current milestone.
- Roadmap tasks (T1 … T8) are labelled `roadmap` and remain open until implementation begins.