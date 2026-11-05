using FluentAssertions;
using OEESentinel.Application.Commands;
using OEESentinel.Application.Handlers;
using OEESentinel.Domain.Entities;
using OEESentinel.Domain.Enums;
using OEESentinel.Domain.Exceptions;
using OEESentinel.Domain.Records;
using OEESentinel.Domain.ValueObjects;
using OEESentinel.UnitTests.Fakes;

namespace OEESentinel.UnitTests.Application;

public sealed class ProcessLogFileHandlerTests
{
    // ── helpers ────────────────────────────────────────────────────────────
    private static readonly MachineCode Maq01 = new("MAQ-01");
    private static readonly DateTimeOffset WindowStart = new(2026, 11, 4, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowEnd   = new(2026, 11, 4, 16, 0, 0, TimeSpan.Zero);

    private static Machine CreateMachine(
        double plannedSeconds = 28800, double idealCycleSeconds = 2.5) =>
        new(Maq01, "Injection Press A1",
            new IdealCycleTime(idealCycleSeconds),
            new Duration(plannedSeconds));

    private static DowntimeReason CreateReason(string code, DowntimeCategory category) =>
        DowntimeReason.Create(new DowntimeReasonCode(code), code, category);

    private static (ProcessLogFileHandler Handler,
                    FakeMachineRepository Machines,
                    FakeProductionShiftRepository Shifts,
                    FakeLogIngestionRepository Ingestions,
                    FakeProductionEventRepository Events,
                    FakeDowntimeReasonRepository Reasons,
                    FakeProductionLogParser Parser,
                    FakeUnitOfWork UoW)
        BuildHandler(Machine machine)
    {
        var machines = new FakeMachineRepository();
        machines.Add(machine);

        var shifts = new FakeProductionShiftRepository();
        var ingestions = new FakeLogIngestionRepository();
        var events = new FakeProductionEventRepository();

        var reasons = new FakeDowntimeReasonRepository();
        reasons.Add(CreateReason("PLANNED_BREAK", DowntimeCategory.PLANNED));
        reasons.Add(CreateReason("PLANNED_MAINTENANCE", DowntimeCategory.PLANNED));
        reasons.Add(CreateReason("UNPLANNED_MECHANICAL", DowntimeCategory.UNPLANNED));
        reasons.Add(CreateReason("UNPLANNED_TOOL_CHANGE", DowntimeCategory.UNPLANNED));
        reasons.Add(CreateReason("UNPLANNED_ELECTRICAL", DowntimeCategory.UNPLANNED));
        reasons.Add(CreateReason("UNPLANNED_OPERATOR", DowntimeCategory.UNPLANNED));

        var parser = new FakeProductionLogParser();
        var uow = new FakeUnitOfWork();

        var handler = new ProcessLogFileHandler(
            machines, shifts, ingestions, events, reasons, parser, uow);

        return (handler, machines, shifts, ingestions, events, reasons, parser, uow);
    }

    private static ProcessLogFileCommand CreateCommand(
        string machineCode = "MAQ-01",
        DateTimeOffset? start = null,
        DateTimeOffset? end = null) =>
        new(new MachineCode(machineCode),
            new TimeRange(start ?? WindowStart, end ?? WindowEnd),
            "sample.csv",
            new MemoryStream(),
            DateTimeOffset.UtcNow);

    // ── 1. happy path ──────────────────────────────────────────────────────
    [Fact]
    public async Task HandleAsync_WithCleanEvents_ReturnsCompletedResponse()
    {
        var (handler, _, shifts, ingestions, events, _, parser, uow) =
            BuildHandler(CreateMachine());

        parser.AddEvent(new ProductionEvent(WindowStart.AddMinutes(1), Maq01, 10, "ERR_OK", 2));
        parser.AddEvent(new ProductionEvent(WindowStart.AddMinutes(2), Maq01, 12, "ERR_OK", 3));
        parser.AddEvent(new ProductionEvent(WindowStart.AddMinutes(3), Maq01, 15, "ERR_OK", 4));

        var result = await handler.HandleAsync(CreateCommand(), CancellationToken.None);

        result.Status.Should().Be("COMPLETED");
        result.RowsProcessed.Should().Be(3);
        result.RowsRejected.Should().Be(0);
        result.ProductionShiftId.Should().NotBeEmpty();

        shifts.Added.Should().HaveCount(1);
        shifts.Added[0].Totals.TotalParts.Should().Be(37);
        shifts.Added[0].Totals.ApprovedParts.Should().Be(37);
        events.AllEvents.Should().HaveCount(3);

        ingestions.Added.Should().HaveCount(1);
        ingestions.Added[0].Status.Should().Be(IngestionStatus.COMPLETED);

        uow.Began.Should().BeTrue();
        uow.Committed.Should().BeTrue();
        uow.RolledBack.Should().BeFalse();
    }

    // ── 2. machine not found ───────────────────────────────────────────────
    [Fact]
    public async Task HandleAsync_WithUnknownMachine_ThrowsMachineNotFound()
    {
        var (handler, _, shifts, ingestions, _, _, _, _) = BuildHandler(CreateMachine());

        var cmd = CreateCommand(machineCode: "MAQ-99");

        Func<Task> act = () => handler.HandleAsync(cmd, CancellationToken.None);

        await act.Should().ThrowAsync<MachineNotFoundDomainException>()
            .Where(e => e.MachineCode == "MAQ-99");

        shifts.Added.Should().BeEmpty();
        ingestions.Added.Should().BeEmpty();
    }

    // ── 3. duplicate window ────────────────────────────────────────────────
    [Fact]
    public async Task HandleAsync_WhenShiftAlreadyExistsForWindow_ThrowsDuplicate()
    {
        var (handler, _, shifts, ingestions, _, _, _, _) = BuildHandler(CreateMachine());

        var existingShift = ProductionShift.Create(
            Maq01,
            new TimeRange(WindowStart, WindowEnd),
            new ProductionTotals(100, 95),
            DowntimeBreakdown.Empty,
            OeeMetrics.Create(Percentage.Zero, Percentage.Zero, Percentage.One),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);

        shifts.ExistingShift = existingShift;

        Func<Task> act = () => handler.HandleAsync(CreateCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<DuplicateShiftDomainException>()
            .Where(e => e.ExistingShiftId == existingShift.Id);

        shifts.Added.Should().BeEmpty();
        ingestions.Added.Should().BeEmpty();
    }

    // ── 4. malformed-only file ─────────────────────────────────────────────
    [Fact]
    public async Task HandleAsync_WithOnlyMalformedLines_PersistsShiftWithZeroTotals()
    {
        var (handler, _, shifts, ingestions, events, _, parser, _) =
            BuildHandler(CreateMachine());

        parser.AddRejection(new RejectedRow(2, "bad-row-1", RejectedReasons.MissingField));
        parser.AddRejection(new RejectedRow(3, "bad-row-2", RejectedReasons.InvalidTimestamp));
        parser.AddRejection(new RejectedRow(4, "bad-row-3", RejectedReasons.QuantityNegative));

        var result = await handler.HandleAsync(CreateCommand(), CancellationToken.None);

        result.Status.Should().Be("COMPLETED");
        result.RowsProcessed.Should().Be(3);
        result.RowsRejected.Should().Be(3);
        result.RejectedSample.Should().HaveCount(3);

        shifts.Added.Should().HaveCount(1);
        shifts.Added[0].Totals.TotalParts.Should().Be(0);
        shifts.Added[0].Totals.ApprovedParts.Should().Be(0);
        shifts.Added[0].Metrics.Quality.Value.Should().Be(1.0);  // neutral per ADR-0006

        events.AllEvents.Should().BeEmpty();
        ingestions.Added[0].Status.Should().Be(IngestionStatus.COMPLETED);
    }

    // ── 5. downtime overflow ───────────────────────────────────────────────
    [Fact]
    public async Task HandleAsync_WhenDowntimeExceedsPlannedTime_ThrowsOverflowAndPersistsFailure()
    {
        // planned = 60s, window = 120s, downtime = 115s → overflow
        var start = new DateTimeOffset(2026, 11, 4, 8, 0, 0, TimeSpan.Zero);
        var end   = start.AddSeconds(120);
        var machine = CreateMachine(plannedSeconds: 60, idealCycleSeconds: 1.0);

        var (handler, _, shifts, ingestions, _, _, parser, uow) = BuildHandler(machine);

        parser.AddEvent(new ProductionEvent(start.AddSeconds(1), Maq01, 1, "ERR_OK", 2));
        parser.AddEvent(new ProductionEvent(start.AddSeconds(5), Maq01, 0, "UNPLANNED_MECHANICAL", 3));
        // no production event after; final gap closes at window end = 115s of downtime

        var cmd = CreateCommand(start: start, end: end);

        Func<Task> act = () => handler.HandleAsync(cmd, CancellationToken.None);

        await act.Should().ThrowAsync<DowntimeOverflowDomainException>();

        shifts.Added.Should().BeEmpty();
        uow.RolledBack.Should().BeTrue();
        uow.Committed.Should().BeFalse();

        ingestions.Persisted.Should().HaveCount(1);
        ingestions.Persisted[0].Status.Should().Be(IngestionStatus.FAILED);
        ingestions.Persisted[0].FailureReason.Should().Be("DOWNTIME_OVERFLOW");
    }

    // ── 6. cancellation ────────────────────────────────────────────────────
    [Fact]
    public async Task HandleAsync_WhenCancelledMidStream_PersistsFailureAndRethrows()
    {
        var (handler, _, shifts, ingestions, _, _, parser, uow) = BuildHandler(CreateMachine());

        parser.AddEvent(new ProductionEvent(WindowStart.AddMinutes(1), Maq01, 10, "ERR_OK", 2));
        parser.AddEvent(new ProductionEvent(WindowStart.AddMinutes(2), Maq01, 10, "ERR_OK", 3));
        parser.AddEvent(new ProductionEvent(WindowStart.AddMinutes(3), Maq01, 10, "ERR_OK", 4));
        parser.ThrowCancellationMidStream = true;

        using var cts = new CancellationTokenSource();

        Func<Task> act = () => handler.HandleAsync(CreateCommand(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();

        shifts.Added.Should().BeEmpty();
        uow.RolledBack.Should().BeTrue();

        ingestions.Persisted.Should().HaveCount(1);
        ingestions.Persisted[0].Status.Should().Be(IngestionStatus.FAILED);
        ingestions.Persisted[0].FailureReason.Should().Be("CANCELLED_BY_CLIENT");
    }
}