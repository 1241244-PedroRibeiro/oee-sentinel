# ADR-0007: Testing Strategy — xUnit + FluentAssertions and a Layered Test Pyramid

- **Status:** Accepted
- **Date:** 2026-11-04
- **Decision Makers:** OEE-Sentinel Development Team

## Context

The OEE math is the product. If Availability, Performance, Quality, or the OEE composite are silently wrong, the dashboard is a liability. Unit tests are the **only** mechanism that guarantees the formula's correctness across every future refactor. Additionally, the ingestion pipeline (parser → command → bulk insert) has many failure paths (`malformed line`, `cancelled upload`, `database unavailable`, `duplicate ingestion`) that must be verified without spinning up PostgreSQL in every test run.

The BYOP submission (`(Ext) BYOP - Project submission instructions.pdf` p.2) requires "signs of use — releases, users, issues, a deployment". An automated test suite with a non-trivial assertion count is one of the strongest signals of *real* use; a repository with zero tests, no CI, and no runnable test command is a strong negative signal.

KB §14 defines the test pyramid (unit / integration / E2E), the F.I.R.S.T. principles, and the Red-Green-Refactor discipline. ADR-0001 established that Domain and Application have zero external dependencies, which means they are *already* unit-testable. This ADR formalises the stack and the current coverage boundary.

## Decision

We adopt a **layered test pyramid** with `xUnit` + `FluentAssertions` as the baseline, and defer integration testing to the roadmap.

### Level 1 — Domain unit tests (current baseline, already implemented)

- **Project:** `tests/OEESentinel.UnitTests/`.
- **Framework:** `xUnit` v2 + `FluentAssertions`.
- **Coverage requirement:** every public method of `OeeCalculatorEngine`, every constructor of every Value Object, and every aggregate factory method that can fail.
- **Existing test file:** `tests/OEESentinel.UnitTests/OeeCalculatorEngineTests.cs`.
- **Property-based testing:** **deferred.** No `FsCheck` in the baseline; property-based tests would have a high learning cost for a marginal verification benefit on a formula that has ~12 discrete cases.

### Level 2 — Application unit tests with hand-written fakes (baseline)

- **Scope:** `ProcessLogFileCommand` handler tested with a `FakeProductionLogParser`, a `FakeMachineRepository`, a `FakeProductionShiftRepository`, a `FakeLogIngestionRepository`. Hand-written `internal sealed class FakeX : IX` — no `Moq`, no `NSubstitute`.
- **Assertion style:** FluentAssertions.
- **What they verify:** the orchestration — that a parser yielding N events produces a shift with the correct totals, that a rejected line increments `RowsRejected` but does not abort the shift, that a cancelled token propagates and calls `markFailed`.

### Level 3 — Integration tests with a real PostgreSQL (deferred to roadmap T6)

- **Scope:** `WebApplicationFactory<Program>` spinning up the WebUI, hitting the DB, running the full upload → compute → persist → query cycle.
- **Project (planned):** `tests/OEESentinel.IntegrationTests/`.
- **Tooling (planned):** `Testcontainers.PostgreSql` (Docker required in CI) **or** a per-test schema on a shared PostgreSQL (`TEST_DATABASE_URL`). The choice is deferred to T6 — the baseline does not need integration tests because the domain math is the risk, and the math is fully unit-tested.
- **Why deferred:** Adding Docker-in-CI complexity to the baseline adds friction to BYOP review. The integration tests are listed in ROADMAP T6 with a 3.0h estimate and will be implemented once the baseline is stable.

### Level 4 — End-to-end UI tests (not in scope)

- Playwright / bUnit are **not** planned for the baseline or the 20-hour roadmap. The UI is a thin consumer of the Application DTOs; bugs in a Razor component are caught during manual review, not by E2E tests. This is a deliberate trade-off: the cost of maintaining E2E tests outweighs their value on a two-screen dashboard.

### F.I.R.S.T. discipline

- **Fast:** Domain tests execute in ~200 ms total. No file I/O, no DB, no network.
- **Independent:** Each test constructs its own fixtures; no shared mutable state.
- **Repeatable:** No dependency on `DateTime.Now`, `Guid.NewGuid()`, or environment variables. The one place where a clock is needed (`ProductionShift.RecordedAt`) is injected by the caller.
- **Self-validating:** Every test has an explicit assertion (`result.Oee.Should().BeApproximately(0.75, 0.001)`).
- **Timely:** Written alongside the code, not after.

### Naming convention

- Test class: `<ClassName>Tests` (e.g., `OeeCalculatorEngineTests`, `ProductionTotalsTests`, `MachineCodeTests`).
- Test method: `<Scenario>_<ExpectedBehavior>` (e.g., `Calculate_WithZeroPlannedTime_ThrowsArgumentOutOfRangeException`, `Calculate_WithPerformanceAboveOne_ClampsTo100Percent`).
- Uses `[Fact]` for single-case, `[Theory]` for parameterised cases (e.g., clamping at 0, 1, 1.5, -0.1).

## Considered Alternatives

### 1. NUnit + `Assert.That`

- **Description:** NUnit 4 + classic asserts.
- **Pros:** Very mature; rich attribute model.
- **Cons:** xUnit has become the de facto standard for ASP.NET Core projects (Microsoft's own templates default to xUnit). `Assert.That(x, Is.EqualTo(y))` is more verbose than `x.Should().Be(y)`. The `xunit.runner.visualstudio` integration is more polished for `dotnet test` in CI.
- **Reason rejected:** xUnit is the ecosystem default; FluentAssertions is more readable.

### 2. `Moq` or `NSubstitute` for unit tests

- **Description:** Use a mocking library for all `I...Repository` and `IProductionLogParser` dependencies.
- **Pros:** Less boilerplate in Application tests; can `Verify(x => x.Add(It.IsAny<...>()))`.
- **Cons:** Mocking a domain port often produces tests that **verify implementation** rather than behavior. A `FakeMachineRepository` (a hand-written `Dictionary<MachineCode, Machine>`) is ~15 lines and expresses the *behaviour* ("this machine exists") rather than the *interaction* ("`GetByCodeAsync` was called once with `MAQ-01`"). For the baseline, hand-written fakes are simpler, more readable, and — critically — they do not couple the test to the port's signature, so refactoring a repository method is a local change.
- **Reason rejected:** Behaviour-based fakes beat interaction-based mocks for this domain.

### 3. `FluentAssertions` vs plain `Assert`

- **Description:** `Assert.Equal(0.75, result.Oee, precision: 3)` vs `result.Oee.Should().BeApproximately(0.75, 0.001)`.
- **Pros of FluentAssertions:** The failure message reads like a sentence. `BeApproximately` has clearer semantics than `Assert.Equal` with a precision argument. `Should().BeInRange(0, 1)` reads naturally.
- **Cons of FluentAssertions:** A commercial license change in 2025 (FluentAssertions v8+) requires a paid license for some uses. **Baseline pins `FluentAssertions` to a version covered by the open-source license (7.x).** Upgrading to v8+ requires a review of the license terms.
- **Reason chosen:** Readable assertions on math are the primary value of the tests; the pin is documented in `.csproj` and `Directory.Packages.props`.

### 4. `FsCheck` (property-based testing) for the calculator

- **Description:** Property tests like "for all `ProductionTotals` with `TotalParts ≥ ApprovedParts ≥ 0`, `Quality ∈ [0,1]`".
- **Pros:** Discover edge cases the developer did not think of.
- **Cons:** High learning cost for a one-developer project. FsCheck's `Arbitrary<T>` for a `record` with multiple invariants requires custom generators. The domain has ~12 discrete cases; a table-driven `[Theory]` covers them.
- **Reason rejected:** Overkill for the domain's size; `[Theory]` covers the cases.

### 5. Integration tests in the baseline (not deferred)

- **Description:** Add `Testcontainers.PostgreSql` + `WebApplicationFactory` from day one.
- **Pros:** Verify the parser→persist→query path end-to-end from the start.
- **Cons:** Docker-in-CI setup (or a shared PostgreSQL in CI) adds a prerequisite that blocks `dotnet test` on a fresh clone. The BYOP reviewer running `dotnet test` on a machine without Docker would see failures. Also, the baseline does not yet have a `ProcessLogFileCommand` implementation — testing it before it exists is premature.
- **Reason deferred:** The domain math is the risk. Integration tests are added (T6) once the vertical slice exists.

## Consequences

### Positive

- **The OEE math is verified by tests that read like documentation.** A reader of `OeeCalculatorEngineTests` sees the formula's edge cases in plain English (`Calculate_WithDowntimeGreaterThanPlanned_ThrowsArgumentOutOfRangeException`).
- **`dotnet test` is the only command needed.** No Docker required to run unit tests. `dotnet test` on a fresh clone is a 3-second operation. This directly signals BYOP "how to run" on a fresh checkout.
- **Every future refactor of the calculator is protected.** If a future change to `OeeCalculatorEngine` (e.g., "add a theoretical maximum capacity parameter") breaks an existing case, the test fails — not the plant manager's month-end report.
- **Hand-written fakes are readable and refactor-safe.** Renaming `IProductionLogParser.ParseAsync` is a local change to the fake, not a global find-and-replace across mock setups.
- **F.I.R.S.T. is met.** Tests are Fast (200 ms), Independent (each `[Fact]` constructs its own fixtures), Repeatable (no clock, no random, no network), Self-validating (FluentAssertions messages), Timely (written with the code).

### Negative / Trade-offs

- **No integration coverage in the baseline.** The upload → parse → persist → query path is verified by `dotnet run` + manual testing in the baseline. This is documented as a roadmap item (T6, ~3h). The trade-off is accepted for BYOP submission speed.
- **No E2E coverage.** Playwright/bUnit deferred indefinitely. Bugs in a Razor component are caught on review; the components are small and reuse `Application` DTOs.
- **`FluentAssertions` version pinned to 7.x.** A future upgrade to 8+ requires a license review. Pinned version documented in `Directory.Packages.props` (introduced in roadmap T7).
- **No `Moq`/`NSubstitute`.** Hand-written fakes add ~15 lines per port, but are readable and refactor-safe. If the Application layer grows to >5 handlers, a mocking library becomes more attractive — deferred.
- **`dotnet test` on the baseline is fast (<5 s), but the eventual integration tests (T6) will require Docker and run in ~30–60 s.** Accepted trade-off.

## References

- KB §14.1 (test pyramid), §14.2 (F.I.R.S.T., test doubles), §9.2 (layered testability).
- `(Ext) BYOP - Project submission instructions.pdf` p.2 ("signs of use — releases, users, issues, a deployment").
- `tests/OEESentinel.UnitTests/OeeCalculatorEngineTests.cs` (existing).
- `README.md` ("Run the tests").
- `ROADMAP.md` (T6 — expanded test suite, T7 — BenchmarkDotNet).
- ADR-0001 (§Positive: "The calculator is a pure static method with no dependencies. Its tests … do not mock anything.").