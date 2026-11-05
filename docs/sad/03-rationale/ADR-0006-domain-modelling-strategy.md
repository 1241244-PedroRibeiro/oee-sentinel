# ADR-0006: Domain Modelling Strategy — Immutable Facts, Value Objects, and a Pure OEE Calculator

- **Status:** Accepted
- **Date:** 2026-11-04
- **Decision Makers:** OEE-Sentinel Development Team

## Context

The OEE formula (from `docs/sad/01-problem-statement.md` §2.3 and KB §7.2) is:

- **Availability** = `OperatingTime / PlannedProductionTime` — clamped to [0,1].
- **Performance** = `(TotalParts × IdealCycleTime) / OperatingTime` — clamped to [0,1].
- **Quality** = `ApprovedParts / TotalParts` — clamped to [0,1]; `1.0` when `TotalParts = 0`.
- **OEE** = `A × P × Q`.

The math is simple but the **edge cases are not**:

- `PlannedProductionTime = 0` → division by zero. Should Availability be 0? 1? A domain error?
- `OperatingTime < 0` (downtime > planned) → data corruption. Reject? Clamp? Both?
- `TotalParts = 0` → Quality is undefined. Mathematically `0/0`. Is the answer 1 (nothing produced → nothing bad) or 0 (nothing produced → total loss)?
- `Performance > 1` (machine reports more parts than physically possible) → corruption or legitimate burst? Clamp?
- `TotalParts < ApprovedParts` → the totals are internally inconsistent. Reject at construction.

Each of these decisions is a **domain invariant**, not a UI concern and not a persistence concern. If they live in the calculator's caller, every caller must re-decide them — and they will drift. If they live in the calculator, they must be testable in isolation without a database, without a mock, and without a file system.

Similarly, `ProductionShift` is a **fact** about what happened in a shift. Once computed and persisted, it must not silently change when the machine's `IdealCycleTime` is later edited, when the downtime catalogue is revised, or when the calculator is updated in a future version. It must be immutable, and corrections must create new rows.

## Decision

We adopt three interlocking domain-modelling policies:

### 1. Value Objects are the unit of validation and immutability

- **`Percentage`** — `double Value ∈ [0,1]`; constructor rejects out-of-range; multiplication and comparisons supported. No `+` operator (percentages are not additive across pillars).
- **`Duration`** — `double Seconds ≥ 0`; constructor rejects negatives.
- **`IdealCycleTime`** — `double Seconds > 0`; constructor rejects ≤ 0.
- **`TimeRange`** — `(DateTimeOffset Start, DateTimeOffset End)`; constructor rejects `Start >= End`.
- **`ProductionTotals`** — `(int TotalParts ≥ ApprovedParts ≥ 0)`. `RejectedParts` is a **computed** property (`TotalParts - ApprovedParts`), not a constructor parameter. This eliminates the "totals are inconsistent" bug class entirely.
- **`OeeMetrics`** — `(Percentage Availability, Percentage Performance, Percentage Quality, Percentage GlobalOee)`. **Factory method only:** `OeeMetrics.CreateFromCalculation(...)`. There is **no public constructor** that accepts arbitrary values. The invariants are preserved by construction.
- **`MachineCode`**, **`EmailAddress`**, **`DowntimeReasonCode`**, **`IngestionId`** — validated identifiers.
- **Observations**: "Percentage preserves full double precision internally. Rounding to 4 decimal places is applied only at (a) the DTO boundary (OeeMetricsDto.FromDomain), and (b) the database persistence layer (NUMERIC(5,4)). Rounding inside the Percentage constructor would break the OEE = A × P × Q invariant, because the product of independently-rounded pillars differs from the rounded product."

All Value Objects are declared as **C# `record` types** for structural equality and value semantics, with constructors that validate in `init` (or in a static factory method). None of them is mutable after construction.

### 2. `ProductionShift` is an immutable fact

- `ProductionShift` has **no public setters**, **no `Update` method**, and **no `[Mutable]` behavior**. Its data is set once at construction from `OeeCalculatorEngine.Calculate(...)`.
- Once persisted, a `ProductionShift` row is **never updated** in the database (no `UPDATE` statements target `production_shifts`).
- Corrections (e.g., "the downtime catalogue was wrong, re-parse this shift") produce a **new** `ProductionShift` with `SupersededBy = oldShift.Id`. The old row is **retained** for audit. The Application layer's `GetMachineOeeQuery` returns the latest non-superseded shift.
- `LogIngestion` is the only aggregate with a **lifecycle** (`PENDING → PROCESSING → COMPLETED | FAILED`). Its state transitions are explicit methods on the aggregate (`markProcessing()`, `markCompleted(rows, rejected)`, `markFailed(reason)`).

### 3. `OeeCalculatorEngine` is a pure domain service

```csharp
public static OeeMetrics Calculate(
    Duration plannedProductionTime,
    Duration totalDowntime,
    ProductionTotals totals,
    IdealCycleTime idealCycleTime)
```

- **No `async`.** No I/O. No clock access. No `Random`. No `DateTime.Now`.
- **No dependency injection.** The engine is a `static` method on a `static class`.
- **Deterministic.** Same inputs → same output, always.
- **Edge case policy (documented in XML docs and unit tests):**
  - `planned == 0` → **throws `ArgumentOutOfRangeException`** (a shift with zero planned time is not a production shift; the caller must not invoke the calculator).
  - `totalDowntime > planned` → **throws `ArgumentOutOfRangeException`** (data corruption; the parser/aggregator must not produce this).
  - `operating == 0` → `Availability = 0`, `Performance = 0`, `Quality = ApprovedParts > 0 ? 0 : 1`, `OEE = 0`.
  - `TotalParts == 0` → `Quality = 1.0` (no production, no rejects → quality is neutral; the *business* interpretation — "no shift occurred" — is a caller concern).
  - `Performance > 1` → **clamped to 1.0**. Documented as defensive: a `Performance = 1.05` indicates the machine reported more parts than `IdealCycleTime` physically allows; the result is recorded with a `Warning` flag on the `ProductionShift` (future roadmap), but the *published* metric is 1.0.
  - `Availability > 1` → impossible by construction (`downtime ≥ 0`), but **defensively clamped**.
  - All four outputs clamped to `[0,1]` after computation.

The engine's test suite (`tests/OEESentinel.UnitTests/OeeCalculatorEngineTests.cs`) covers all the above cases. Every test is a one-liner `[Fact]` with `FluentAssertions`.

## Considered Alternatives

### 1. Calculator as an instance class with `IHostEnvironment`/`IClock` dependencies

- **Description:** Inject a clock, a logger, and a configuration to make "testable".
- **Pros:** Supports future features like "holidays", "shift templates loaded from config", etc.
- **Cons:** The calculator does not need any of those. Adding a dependency it does not use is the **Interface Segregation anti-pattern**. Testing becomes "mock the clock, mock the logger" — pure ceremony. KB §7.2 explicitly names the calculator as a *pure domain service*: *"lógica de negócio que não pertence naturalmente a nenhuma Entidade ou Value Object. É stateless."*
- **Reason rejected:** Violates the pure-service intent.

### 2. Calculator embedded in `ProductionShift` (rich aggregate, no separate service)

- **Description:** `ProductionShift.ComputeMetrics()` method on the aggregate.
- **Pros:** All OEE logic in one place; the aggregate is truly *rich*.
- **Cons:** The aggregate then depends on `Machine.IdealCycleTime` — which lives in a *different* aggregate. The aggregate would need to load `Machine`, violating ADR-0001's cross-aggregate reference rule (`MachineId`, not `Machine`) and KB §3.2's rule #2. The calculator also needs `PlannedProductionTime`, which is derived from `Machine.PlannedShiftDuration` minus planned downtime in `DowntimeBreakdown` — again, data from outside the `ProductionShift` aggregate. The `OeeCalculatorEngine` is explicitly listed in ADR-0001's Domain layer precisely because the calculation spans multiple aggregates' data.
- **Reason rejected:** Would force cross-aggregate coupling.

### 3. Calculator in Application (a `ShiftComputationService`)

- **Description:** Put the OEE math in an Application service that orchestrates the calculation.
- **Pros:** Keeps Domain clean of computation; makes the calculation a "use case".
- **Cons:** The OEE formula is a **domain invariant**, not a use case. It is the product's core value. It must be testable with zero infrastructure. In Application, the calculator would depend on `IDateTimeProvider` (for shift bounds) and would be tempted to call a repository — the exact coupling that ADR-0001 is designed to prevent.
- **Reason rejected:** Misclassifies a domain concern as a use case.

### 4. `ProductionShift` mutable (corrections update in place)

- **Description:** If a shift is wrong, update the row.
- **Pros:** Smaller storage. Simpler "latest" semantics.
- **Cons:** Audit is gone. Historical OEE can silently change. The `ProductionShift` fact is what a plant manager attests to; mutating it is a data integrity violation. The `SupersededBy` design has a small storage cost (one extra row per correction) and an enormous audit benefit.
- **Reason rejected:** Violates the fact-like nature of `ProductionShift`.

### 5. `RejectedParts` as a constructor parameter

- **Description:** `ProductionTotals(TotalParts, ApprovedParts, RejectedParts)`.
- **Pros:** Symmetric API. Symmetric DB columns.
- **Cons:** Creates the possibility of an inconsistent triple (`5, 2, 1` — three contradicting numbers). Every caller must compute `Rejected = Total - Approved`, and every test must verify it. Making `RejectedParts` a computed property eliminates the class of bug.
- **Reason rejected:** Elimination of a bug class by construction.

### 6. C# `readonly struct` for Value Objects

- **Description:** `readonly struct Percentage` instead of `record Percentage`.
- **Pros:** Zero heap allocation. Better perf for hot loops (e.g., parsing 200k rows).
- **Cons:** Struct-based validation in `record`-like syntax is more verbose (no primary constructors until C# 12 for records). The perf benefit is measurable only in a hot inner loop (the parser), and the parser does not need `Percentage` — it produces `ProductionEvent` with `int quantity` and `string errorCode`. The domain VOs are created once per shift, not per row. `record` (reference type) is simpler and has structural equality for free.
- **Reason rejected:** Premature optimisation; `record` is idiomatic.

## Consequences

### Positive

- **The OEE math is verified by the compiler and the tests, not by the caller.** Any `Percentage`, `Duration`, or `ProductionTotals` instance that exists is valid by construction. No "defensive checks" everywhere.
- **`OeeCalculatorEngine.Calculate` can be called from a unit test without any setup.** The existing test file is a suite of `[Fact]` methods, each a single call.
- **`ProductionShift` is safe to cache, memoise, and serialize.** Because it is immutable, concurrent readers cannot see a torn state. The EF Core mapping (owned types, no navigation properties) is straightforward.
- **Corrections preserve history.** A plant manager can answer "what was the reported OEE last week?" even if the inputs changed. This directly serves `docs/sad/01-problem-statement.md` §2.4 (auditability).
- **`ApprovedParts ≤ TotalParts` cannot be violated.** Enforced by `ProductionTotals`'s single constructor, by the DB `CHECK` (ADR-0003), and by the absence of setters.

### Negative / Trade-offs

- **Two `ProductionShift` rows for corrections.** Storage cost of a correction is a row, not an update. Acceptable.
- **`OeeCalculatorEngine` throws on `planned == 0` and `downtime > planned`.** Callers must guard. In the `ProcessLogFileCommand`, this means "if the shift has zero planned time, record `LogIngestion` as `FAILED` with a domain error message" — an explicit, tested path.
- **`ProductionShift` has no mutable `Update` method.** A future feature "annotate a shift with an operator note" would need a new `ProductionShiftNote` aggregate or a new fact, not an `Update`. This is a design constraint that must be respected, not overcome.
- **The clamping behaviour in `OeeCalculatorEngine` can be perceived as "hiding" data corruption.** The trade-off is documented: clamping produces a defensible published number; corruption detection is delegated to future roadmap (a `Warning` flag on `ProductionShift`, T7). The baseline records `Performance > 1` in a future log but does not persist it. The decision is: **defensible published number over silent exception**.
- **`record` types may box in some contexts** (e.g., `IEquatable<T>` dispatch). Negligible for the domain.

## References

- KB §3.1 (attribute vs VO, VO immutability), §3.2 (rich entities, no public setters), §3.3 (aggregate boundaries), §7.2 (domain services), §13.1 (Entity vs VO decision guide).
- `docs/sad/01-problem-statement.md` §2.2, §2.3, §2.4, §3.
- `docs/domain/domain-model.md` §2–§5.
- `tests/OEESentinel.UnitTests/OeeCalculatorEngineTests.cs` (existing test file).
- `src/OEESentinel.Domain/ValueObjects/OeeMetrics.cs`, `src/OEESentinel.Domain/Services/OeeCalculatorEngine.cs` (already implemented; this ADR documents the policy).