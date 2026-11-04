# Testing Strategy — OEE-Sentinel

This document operationalises the decisions recorded in **ADR-0007 (Testing Strategy)**. It describes how the three test tiers are structured, named, and executed across the baseline and the roadmap.

## 1. The Test Pyramid (Applied)

| Tier | Scope | Tools | Speed | Execution |
|---|---|---|---|---|
| **Domain** | Pure business logic (VOs, Aggregates, `OeeCalculatorEngine`) | xUnit + FluentAssertions | ~200ms | `dotnet test` (no Docker) |
| **Application** | Handlers (`ProcessLogFileHandler`, etc.) | xUnit + hand-written Fakes | ~500ms | `dotnet test` (no Docker) |
| **Integration** *(T6)* | Full HTTP + EF Core + PostgreSQL | `WebApplicationFactory`, Testcontainers | 30-60s | `dotnet test` (requires Docker) |
| **UI (bUnit)** *(T6)* | Blazor components (`OeeGauge`, `UploadDropzone`) | bUnit | ~1s | `dotnet test` |

## 2. Naming Conventions

- **Test Class:** `<ClassName>Tests` (e.g., `OeeCalculatorEngineTests`, `ProcessLogFileHandlerTests`).
- **Test Method:** `<Scenario>_<ExpectedBehavior>` (e.g., `Calculate_WhenDowntimeExceedsPlanned_ThrowsArgumentOutOfRangeException`).
- **Arrange-Act-Assert:** Each test must be clearly divided into these three blocks (or use FluentAssertions' `act.Should().Throw<...>()` pattern for concise tests).

## 3. Domain Layer Strategy

**Objective:** Prove the mathematical and invariant correctness of the core domain.
**Do not use:** Mocks, databases, or file systems.

- **Value Objects:** Test the constructor boundaries (e.g., `Percentage(1.01)` throws; `Percentage(0.5)` succeeds).
- **`OeeCalculatorEngine`:** Test all edge cases documented in ADR-0006 (zero planned time, downtime overflow, clamping, zero produced, perfect production).
- **Aggregates:** Test lifecycle methods (`LogIngestion.markFailed` sets the reason, `Machine.deactivate` sets `is_active = false`).

## 4. Application Layer Strategy

**Objective:** Prove orchestration logic without infrastructure dependencies.
**Do not use:** The real `DbContext` or the real `CsvHelper`.

- **Fakes (not Mocks):** Implement in-memory versions of the ports (e.g., `FakeMachineRepository` backed by a `Dictionary`).
- **Parser:** Use a `FakeProductionLogParser` that yields pre-defined `ProductionEvent` lists (e.g., one for happy path, one with a cancelled token).
- **Assertions:** Assert on the resulting DTOs and on the state of the Fakes (e.g., `fakeShiftRepo.WasAddCalled`).

## 5. Integration & E2E Strategy (Roadmap T6)

**Objective:** Prove that the HTTP boundary, EF Core mappings, and PostgreSQL constraints work together.

- **Database Tests:** Use `Testcontainers.PostgreSql` to spin up a real DB per test run. Assert that `ON DELETE RESTRICT` works, that the partial unique index `uq_production_shifts_current` rejects duplicates, and that JSONB validation functions fire.
- **API Tests:** Use `WebApplicationFactory<Program>` to call `POST /api/v1/logs` and `GET /api/v1/machines/{code}/oee`, asserting on HTTP status codes and JSON payloads (contract testing).
- **Blazor Tests:** Use `bUnit` to render `OeeGauge.razor` and assert that the percentage is rendered correctly (e.g., `0.753` renders as `75.3%`).

## 6. CI/CD Execution

- **Local:** `dotnet test` runs Domain + Application (fast feedback).
- **CI (T8):** GitHub Actions runs `dotnet test` on every PR. Database tests are cached via Docker layer caching.
- **Coverage:** Minimum 90% on `OEESentinel.Domain`, 70% on `OEESentinel.Application`. Enforced via `coverlet`.

## 7. Test Data Strategy

- **Domain:** Hardcoded values inside the test method (no shared fixtures).
- **Application:** Hardcoded Fakes.
- **Integration:** The `samples/sample_shift.csv` file is the canonical fixture. Additional fixture files are generated on-the-fly inside the test using `MemoryStream` to simulate 200k rows.