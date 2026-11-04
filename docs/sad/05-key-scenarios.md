# Key Architectural Scenarios — OEE-Sentinel

## 1. Introduction and Purpose

Key architectural scenarios capture the **critical functional paths**, **high-risk operational areas**, and **quality-attribute intersections** that validate the design against non-functional drivers (KB §2.2, §6.1). For OEE-Sentinel, the critical scenarios exercise:

1. **Streaming ingestion at industrial volume** (200k rows, bounded memory).
2. **OEE mathematical correctness at the edge cases** (zero produced, downtime overrun).
3. **Immutability and auditability of computed facts** (`ProductionShift` supersession).
4. **Analytical reads across a window** (Pareto aggregation without N+1).
5. **Auth and RBAC as a planned evolution** (deferred; documented, not implemented).

The baseline scenarios are **executable today** (they map to `tests/` or manual verification steps). Scenario 5 is explicitly **planned** (roadmap T2) and is included to show how the architecture accommodates the roadmap without touching the current three pillars.

---

## 2. Key Scenario 1: Streaming Ingestion of a 200k-Line Shift Log

**Trigger:** A plant operator drops a `shift_2026-11-04_MAQ-01.csv` file into the Blazor upload page.

**Architectural drivers:** Performance (bounded memory, sub-5s ingestion), Reliability (per-row rejection without abort), Maintainability (parser swappable).

**Flow (Blazor path — in-process):**

1. `Upload.razor` binds `IBrowserFile` and calls `IProcessLogFileHandler.HandleAsync(new ProcessLogFileCommand(machineCode, shiftWindow, fileStream))`.
2. `ProcessLogFileHandler` (Application) validates `MachineCode` via `IMachineRepository.GetByCodeAsync` — rejects unknown machines with `MachineNotFoundDomainException`, mapped by the endpoint/handler to a `ProblemDetails` or a UI error card.
3. `LogIngestion` is created in state `PENDING` and persisted via `ILogIngestionRepository.AddAsync`. Its typed ID is returned immediately.
4. Handler opens `IProductionLogParser.ParseAsync(stream, cancellationToken)` — a `CsvProductionLogParser` in Infrastructure. The stream is a `FileStream` wrapping the `IBrowserFile.OpenReadStream()`, chunked by Blazor's SignalR transport (typically 20KB-100KB chunks, transparently joined by `IBrowserFile.OpenReadStream`).
5. `await foreach (var evt in parsed)` consumes events. The handler buffers `ProductionEvent` rows in batches of 5000. On each batch fill, it calls `IProductionEventRepository.BulkInsertAsync(batch)` (which uses `EFCore.BulkExtensions.PostgreSql`).
6. Concurrently, the handler accumulates: `totalParts`, `approvedParts` (derived from `errorCode` not present in rejected set), `downtimeByReason` (dictionary of `DowntimeReasonCode → Duration`, computed from inter-event gaps and error codes), and `rejectedRowsSample` (bounded to 100 entries).
7. On `IAsyncEnumerable` completion, handler invokes `OeeCalculatorEngine.Calculate(plannedTime, totalDowntime, totals, idealCycleTime)` — throws if `planned == 0` or `downtime > planned` (structural corruption from the file).
8. Handler constructs the `ProductionShift` aggregate with the computed `OeeMetrics`, the `DowntimeBreakdown` VO, and the `sourceIngestionId`.
9. In a single EF Core transaction: `BulkInsertAsync` flushes the remaining buffer, `IProductionShiftRepository.AddAsync(shift)` writes the shift, and `LogIngestion.markCompleted(rowsProcessed, rowsRejected)` transitions state to `COMPLETED`. `SaveChangesAsync` commits.
10. Handler returns `LogUploadResponseDto` with `ingestionId`, `status = COMPLETED`, `rowsProcessed`, `rowsRejected`, `rejectedSample`, and the `ProductionShift` id.
11. `Upload.razor` navigates to `/machines/{code}/shifts/{id}` (the shift detail page) and renders the gauges.

**What must be true for this scenario to pass:**

- Memory footprint stays under 50 MB peak RSS on a 200k-row file (verified by BenchmarkDotNet under roadmap T7).
- A malformed line (missing comma, bad decimal locale, truncated timestamp) increments `rowsRejected` but does not abort the whole batch. `CsvHelper` config: `BadDataFound = null`, `MissingFieldFound = null`, plus `TryMapToDomain` guard.
- The whole operation completes in < 5 seconds for a 200k-row file on the reference machine (documented in README).
- A cancelled HTTP request (user navigates away, Blazor circuit drops) propagates `CancellationToken` through `ParseAsync` and `BulkInsertAsync`, and `LogIngestion.markFailed(reason)` is persisted. The transaction is not partially committed.

**Architectural validation:**

- Confirms `IProductionLogParser` is truly a port: replacing CsvHelper with a future `Sep`-based or MQTT-based parser is a one-class change in Infrastructure (ADR-0005).
- Confirms bulk insert is not part of the domain: `BulkInsertAsync` is an `IProductionEventRepository` method, called from Application. The `ProductionEvent` domain record has no EF attributes.
- Confirms bounded memory: no `File.ReadAllLines` anywhere; parser and bulk-insert use streaming.

**Reference:** ADR-0001, ADR-0003, ADR-0005, ADR-0006.

---

## 3. Key Scenario 2: OEE Math at the Edge Cases (Structural Corruption)

**Trigger:** A malformed file (or a misconfigured `Machine`) produces one of the following conditions:

| Condition | Consequence in a naive calculator |
|---|---|
| `plannedTime == 0` | Division by zero → `Infinity` or `NaN` in DB → all downstream queries fail |
| `totalDowntime > plannedTime` | Negative `OperatingTime` → `Availability > 1` → dashboard shows >100% |
| `totalParts == 0` | `Quality = 0/0` → NaN |
| `performance > 1` (machine reports more parts than physically possible) | Dashboard shows >100% Performance → not defensible to plant manager |
| `approvedParts > totalParts` (parser bug) | `Quality > 1` → not defensible |

**Architectural drivers:** Reliability (calculated result must be trustworthy), Usability (defensible numbers on the dashboard), Correctness.

**Flow:**

1. `OeeCalculatorEngine.Calculate` receives the four input parameters (ADR-0006):
   - `Duration plannedProductionTime`
   - `Duration totalDowntime`
   - `ProductionTotals totals`
   - `IdealCycleTime idealCycleTime`
2. `ProductionTotals` was constructed by the Application handler. Its constructor rejects `TotalParts < ApprovedParts` and `TotalParts < 0` and `ApprovedParts < 0`. So condition "approvedParts > totalParts" never reaches the calculator — it is caught at construction with a `DomainInvariantException`.
3. The calculator's policy on the remaining edge cases:
   - `planned == 0` → `throw ArgumentOutOfRangeException`. The Application handler maps this to a `LogIngestion.FAILED` with reason `"PLANNED_TIME_ZERO"`. The `ProductionShift` is not persisted.
   - `totalDowntime > planned` → `throw ArgumentOutOfRangeException`. Handler maps to `FAILED` with `"DOWNTIME_OVERFLOW"`.
   - `operating == 0` (downtime == planned, everything stopped) → `Availability = 0`, `Performance = 0`, `Quality = ApprovedParts > 0 ? 0 : 1`. The shift is persisted with these values, and a future `Warning` flag (roadmap T6) marks it for review.
   - `TotalParts == 0` → `Quality = 1.0` (neutral). The shift is persisted; a future `Warning` flag is set.
   - `Performance > 1` → **clamped to 1.0**. Persisted with the clamping visible in a future `Warning` flag (T6). The formula is documented as defensive: a Performance > 1 indicates upstream data corruption; the *published* number is 1.0.
   - All four output `Percentage` values clamped to `[0,1]` after computation.

**What must be true for this scenario to pass:**

- `OeeCalculatorEngineTests` covers each row of the table above with a `[Theory]` or `[Fact]`. Existing tests already cover the happy path; the edge cases are added in the baseline before submission.
- No unhandled `Infinity`, `NaN`, or negative value reaches the DB. The `CHECK` constraints (`availability BETWEEN 0 AND 1`, etc.) act as a second line of defence — if the calculator ever produces an invalid value, the write fails loudly rather than silently persisting corruption.
- The dashboard (Blazor) renders `100%` and not `>100%` for any clamped value. Chart.js gauge max is `100`.
- A `LogIngestion.FAILED` with a domain-readable reason is produced for every rejected file, and it is visible in `/ingestions` (Blazor page).

**Architectural validation:**

- Confirms the calculator is a **pure domain service** — the edge-case policy is testable with a single method call and FluentAssertions, no fixtures (ADR-0006).
- Confirms the **defence-in-depth** between domain and DB — the CHECK constraints are not redundant, they are a safety net for future code paths that might bypass the calculator.

**Reference:** ADR-0003, ADR-0006, ADR-0007.

---

## 4. Key Scenario 3: Immutable Shift Correction via Supersession

**Trigger:** An engineer discovers that a `DowntimeReason` was miscategorised — `UNPLANNED_MECHANICAL` was really `PLANNED_MAINTENANCE`. She needs to correct the historical shift.

**Architectural drivers:** Auditability, Data Integrity, Correctness of historical reports.

**Flow:**

1. Engineer updates the `DowntimeReason` catalogue (via a future admin UI, T2+). The change is metadata only — it does **not** retroactively modify existing `ProductionShift` rows (they hold the `DowntimeBreakdown` as computed at ingestion time, ADR-0006).
2. Engineer re-uploads the original log with a corrected mapping. The `ProcessLogFileHandler` runs as in Scenario 1 and produces a new `ProductionShift`.
3. Before persisting the new shift, the handler queries `IProductionShiftRepository.FindActiveByMachineAndWindowAsync(machineId, window)`. If a current (non-superseded) shift exists for the same window:
   - The handler creates the new shift with `SupersededBy = null` (it is now current).
   - The old shift's `SupersededBy` is updated to point to the new shift's `id`, **via a single `UPDATE production_shifts SET superseded_by_id = @newId WHERE id = @oldId`** — a metadata-only update.
   - The computed OEE values of the old shift are untouched (they remain the immutable fact they were at the time of ingestion).
4. If no current shift exists (first ingestion for that window), the new shift is persisted with `SupersededBy = null`.
5. `GET /api/v1/machines/{code}/oee?from=...&to=...` returns only shifts where `superseded_by_id IS NULL` — the "current" view. A future `?includeSuperseded=true` (T4) exposes the full history for audit.
6. The Blazor `MachineDetail.razor` shows the current shift plus a "history" expander that lists superseded shifts with links, when the user opts in.

**What must be true for this scenario to pass:**

- The computed OEE values (A, P, Q, OEE) and `ProductionTotals` of the superseded shift **never change after persist**. The `UPDATE` is strictly on `superseded_by_id`.
- A superseded shift is never returned by the default `GetMachineOeeQuery`.
- The `LogIngestion` for the correction is a **new** `LogIngestion`, linked to the new shift. The old `LogIngestion` remains `COMPLETED` and its `ProductionShift` is superseded — audit trail preserved.
- A concurrent correction (two engineers uploading the same window simultaneously) is handled by a unique partial index: `CREATE UNIQUE INDEX uq_production_shift_current ON production_shifts (machine_code, window_start, window_end) WHERE superseded_by_id IS NULL`. The second `INSERT` fails with a `409 Conflict`-mappable exception.

**Architectural validation:**

- Confirms `ProductionShift` is a **fact**, not a mutable view. Corrections do not silently change history (ADR-0006).
- Confirms the `SupersededBy` field is documented as the **only** exception to full-row immutability (ADR-0006 addendum note).
- Confirms the partial unique index is the DB-level enforcement of the "one current shift per window" invariant, which is a domain invariant expressed in schema.

**Reference:** ADR-0003, ADR-0006.

---

## 5. Key Scenario 4: Downtime Pareto Analysis across a Window (Read-Side)

**Trigger:** Plant manager opens the Pareto page and selects "Machine MAQ-01, last 7 days".

**Architectural drivers:** Performance (fast read, no N+1), Analytical correctness (Pareto percentages sum to 100% of downtime).

**Flow (Blazor path):**

1. `Pareto.razor` injects `IGetDowntimeParetoHandler` and calls `HandleAsync(new GetDowntimeParetoQuery(machineCode, from, to))`.
2. Handler validates `from < to` and `(to - from) <= 90 days` (guard against unbounded scans).
3. Handler queries `IProductionShiftRepository.FindActiveByMachineAndWindowAsync(machineId, from, to)` — `AsNoTracking()`, returns a `List<ProductionShift>`.
4. Handler aggregates in memory across the list: `Dictionary<DowntimeReasonCode, Duration>` summed from each shift's `DowntimeBreakdown` VO. The total downtime is `Σ entries`.
5. Handler maps to `List<ParetoEntryDto>` with each entry's `Duration`, `Percentage = entry.Duration / totalDowntime` and `Category = PLANNED | UNPLANNED` (looked up from `IDowntimeReasonRepository` for the codes present).
6. Handler sorts by `Duration DESC` and takes the top 20. Returns a `DowntimeParetoDto` with the entries plus `TotalDowntime`, `TotalShifts`, and `Window`.
7. `Pareto.razor` renders a horizontal bar chart via Chart.js (`ParetoChart.razor` + `IJSRuntime`).

**Why in-memory aggregation, not SQL aggregation:**

- The `downtime_breakdown` column is JSONB. PostgreSQL *can* aggregate over JSONB with `jsonb_each_text()` and `SUM`, but the query is significantly more complex than the in-memory aggregation, and the number of shifts in a 90-day window is bounded (≤ 90 shifts × 24h × ≤ 20 entries = ≤ 43k rows — trivial in memory).
- Roadmap T4 introduces a SQL view `v_machine_downtime_pareto` when the volume justifies it. Baseline in-memory is documented as sufficient.
- This honours the *Simplicity over Formalism* principle (ADR-0001 §3): no premature read-model split.

**What must be true for this scenario to pass:**

- Pareto percentages sum to 1.0 (or 100% display), verified by a unit test on the handler.
- Window guard rejects `to - from > 90 days` with a domain error surfaced as `ProblemDetails` or Blazor error card.
- Empty result set (`TotalShifts == 0`) renders an empty-state card: "No shifts in this window".
- The aggregation is `O(shifts)` — no per-shift repository call. A single `FindActiveByMachineAndWindowAsync` returns all shifts.

**Architectural validation:**

- Confirms the `DowntimeBreakdown` VO is a **value** — a `Dictionary<DowntimeReasonCode, Duration>` that composes cleanly across shifts without any aggregate boundary breach.
- Confirms the Pareto aggregation belongs in the **Application layer**, not in the Domain. It is a read-model concern; the OEE math is done by the calculator (Domain), but summing shifts over a window is a query.

**Reference:** ADR-0001, ADR-0003, ADR-0006.

---

## 6. Key Scenario 5 (Planned): JWT-Protected Upload with Role Authorization

> **Status:** Planned under roadmap T2. Not implemented in baseline. Included here to demonstrate that the architecture accommodates the roadmap without domain changes.

**Trigger (post-T2):** An operator authenticates, receives a JWT, and uploads a file. An engineer tries to upload to a different machine's ingestion endpoint — must be rejected.

**Planned flow:**

1. `POST /api/v1/auth/login` (new controller in `WebUI`) validates credentials against `User.password_hash` (Argon2id), issues a JWT with claims `sub`, `role`, `exp = 8h`.
2. `POST /api/v1/logs` now requires `Authorization: Bearer <token>`. A new `AuthorizationMiddleware` in `WebUI` validates the token and mounts `ClaimsPrincipal` on `HttpContext.User`.
3. `ProcessLogFileHandler` (Application) is wrapped by a `WebUI`-level filter that checks `RequireRole(UserRole.OPERATOR | UserRole.ENGINEER | UserRole.ADMINISTRATOR)`.
4. The `ProcessLogFileCommand` **gains a `RequestedByUserId` field**. The handler uses it only for audit — `LogIngestion` gains a `RequestedByUserId` field in a future migration.
5. Blazor circuits use a `AuthenticationStateProvider` backed by the same JWT, stored in an `HttpOnly` cookie or a server-side circuit state (decision deferred to T2's own ADR).

**What the baseline already enables for this scenario:**

- The `User` aggregate exists in Domain (`UserId`, `EmailAddress`, `PasswordHash`, `DisplayName`, `Role`, `IsActive`) even though no auth is wired. The EF Core mapping is written. T2 adds no schema change.
- `UserRole` enum (`OPERATOR`, `ENGINEER`, `ADMINISTRATOR`) is defined.
- The Application handlers take a `CancellationToken`, not a `ClaimsPrincipal`. Adding auth is a `WebUI`-level change (filters, middleware) — Application and Domain remain untouched.
- The composition root (`Program.cs`) is the only place that wires auth services (`AddAuthentication`, `AddAuthorization`).

**Architectural validation:**

- Confirms the **baseline ↔ T2 seam** is at the `WebUI` boundary. Domain and Application are auth-agnostic by design.
- Confirms that **shipping the `User` aggregate in baseline** was a good call — it lets the T2 migration be purely additive (no auth-aware refactoring).

**Reference:** ADR-0001, ADR-0002 (roadmap T2), ADR-0006 (`User` aggregate), `ROADMAP.md`.

---

## 7. Comparative Log: Initial vs. Current

### Section 2 (Scenario 1, Step 4)

- **Initial (before ADR-0005):** "Handler opens `File.OpenRead(path)` and calls `File.ReadAllLines` to parse."
- **Current:** "Handler passes `IBrowserFile.OpenReadStream()` to `IProductionLogParser.ParseAsync(stream, ct)` — a streaming `IAsyncEnumerable<ProductionEvent>`. No `ReadAllLines`."
- **Justification:** `File.ReadAllLines` violates the bounded-memory driver (200k lines × ~100 bytes/line = 20 MB just for strings, plus array and char[] overhead ≈ 200+ MB). ADR-0005 rejected it.

### Section 4 (Scenario 3, Step 3)

- **Initial (before ADR-0006 revision):** "The old `ProductionShift` is updated in place with the new values."
- **Current:** "The old shift's computed values are **never touched**. Only its `superseded_by_id` field is updated, once, to link to the new shift. The old row remains a valid historical fact."
- **Justification:** In-place mutation of the OEE values violates auditability (`docs/sad/01-problem-statement.md` §2.4) and would make the shift unusable as an audit record. ADR-0006 revision clarified the immutability contract.

### Section 5 (Scenario 4, aggregation strategy)

- **Initial:** "Pareto is computed in a SQL view joined to `downtime_breakdown` table (child rows)."
- **Current:** "In-memory aggregation in the Application handler. JSONB storage of `downtime_breakdown` makes SQL aggregation complex; the volume (≤ 43k entries in 90 days) is trivial in memory. SQL view deferred to T4."
- **Justification:** Simplicity over Formalism. The SQL view adds ceremony and migration risk for a problem that does not exist at the baseline scale.