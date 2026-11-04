# Architecture Decision Records — Index

**Project:** OEE-Sentinel
**Folder:** `docs/sad/adrs/`
**Count:** 7 ADRs (baseline phase)
**Companion:** `docs/glossary.md`, `docs/domain/domain-model.md`, `docs/sad/01-problem-statement.md`, `docs/sad/02-context.md`

---

## 1. What is an ADR

An Architecture Decision Record captures a *significant* architectural decision:
its context, the alternatives considered, the decision, and the consequences
(positive and negative). The code says **how**; the ADR says **why**.

Format follows KB §19.1:

- **Status** — Proposed / Accepted / Superseded / Deprecated.
- **Context** — the problem and the forces at play.
- **Decision** — what was decided and how it is implemented.
- **Alternatives** — what else was considered and why it was rejected.
- **Consequences** — what the team gains and what it trades away.

---

## 2. Baseline index

| ADR | Decision | Status | Date |
|---|---|---|---|
| `ADR-0001-architectural-style.md` | Layered Clean/Onion + Hexagonal Ports & Adapters | **Accepted** | 2026-11-04 |
| `ADR-0002-technology-stack.md` | C# / .NET 10 / ASP.NET Core stack | **Accepted** | 2026-11-04 |
| `ADR-0003-persistence-strategy.md` | PostgreSQL 16 + EF Core 10 + `EFCore.BulkExtensions` | **Accepted** | 2026-11-04 |
| `ADR-0004-ui-strategy.md` | Blazor Server (interactive server components) | **Accepted** | 2026-11-04 |
| `ADR-0005-log-parsing-strategy.md` | CsvHelper + `IAsyncEnumerable<ProductionEvent>` | **Accepted** | 2026-11-04 |
| `ADR-0006-domain-modelling-strategy.md` | Immutable `ProductionShift`, Value Objects, pure `OeeCalculatorEngine` | **Accepted** | 2026-11-04 |
| `ADR-0007-testing-strategy.md` | xUnit + FluentAssertions, layered test pyramid | **Accepted** | 2026-11-04 |

---

## 3. Reading order for reviewers

If you have ~20 minutes, read them in this order (each builds on the previous):

1. **ADR-0001** — establishes the Dependency Rule that every other ADR obeys.
2. **ADR-0002** — establishes the stack (C# / .NET 10 / ASP.NET Core).
3. **ADR-0003** — establishes why PostgreSQL (and not MongoDB / SQLite) and how it is accessed (EF Core).
4. **ADR-0006** — establishes *how* the domain is modelled (immutable facts, pure calculator). This is the **product**.
5. **ADR-0005** — establishes how raw logs become domain events (streaming parse).
6. **ADR-0004** — establishes the UI layer (Blazor Server).
7. **ADR-0007** — establishes how all of the above is verified.

---

## 4. Cross-cutting navigation

| Concern | ADR(s) |
|---|---|
| Overall architecture style | **ADR-0001** |
| Runtime, language, core libraries | **ADR-0002** |
| Database engine and ORM | **ADR-0003** |
| UI / frontend framework | **ADR-0004** |
| Streaming ingestion / parser | **ADR-0005** |
| Domain model, immutability, OEE math | **ADR-0006** |
| Testing approach | **ADR-0007** |

---

## 5. Deferred ADRs (roadmap)

The following decisions are deliberately deferred to the 20+ hour roadmap
(`ROADMAP.md` T1–T8) and will be captured as new ADRs when they begin:

| Planned ADR | Subject | Roadmap | Status |
|---|---|---|---|
| ADR-0008 | PDF report export (QuestPDF) | T1 | Pending |
| ADR-0009 | JWT authentication + RBAC | T2 | Pending |
| ADR-0010 | Folder watcher (`FileSystemWatcher` BackgroundService) | T3 | Pending |
| ADR-0011 | Pareto analysis dashboard (CQRS read view) | T4 | Pending |
| ADR-0012 | OpenMetrics / Prometheus endpoint | T5 | Pending |
| ADR-0013 | `IProductionLogParser` — additional formats (TXT, JSON) | T6 | Pending |
| ADR-0014 | Optimistic concurrency on `Machine` | T6 | Pending |

---

## 6. Supersession log

| Superseded ADR | Superseded by | Effective | Reason |
|---|---|---|---|
| *(none yet)* | — | — | — |

**Rule.** An ADR is **never** edited to erase a superseded decision. The history
is preserved; the Status field and this log are the source of truth for what is
current.

---

## 7. References

- KB §19 (ADR template and methodology).
- `docs/sad/01-problem-statement.md`, `docs/sad/02-context.md`, `docs/glossary.md`, `docs/domain/domain-model.md`.
- `ROADMAP.md`.