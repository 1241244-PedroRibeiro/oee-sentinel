using OEESentinel.Application.Commands;
using OEESentinel.Application.Dtos;
using OEESentinel.Application.Ports.Inbound;
using OEESentinel.Application.Ports.Outbound;
using OEESentinel.Domain.Entities;
using OEESentinel.Domain.Exceptions;
using OEESentinel.Domain.Records;
using OEESentinel.Domain.Services;
using OEESentinel.Domain.ValueObjects;

namespace OEESentinel.Application.Handlers;

public sealed class ProcessLogFileHandler : IProcessLogFileHandler
{
    private const double GraceSeconds = 3600.0;
    private const int BatchSize = 5000;
    private const int MaxRejectedSample = 100;
    private const string SentinelNoError = "ERR_OK";
    private const string SentinelScrap = "QUALITY_SCRAP";

    private readonly IMachineRepository _machineRepo;
    private readonly IProductionShiftRepository _shiftRepo;
    private readonly ILogIngestionRepository _ingestionRepo;
    private readonly IProductionEventRepository _eventRepo;
    private readonly IDowntimeReasonRepository _downtimeRepo;
    private readonly IProductionLogParser _parser;
    private readonly IUnitOfWork _uow;

    public ProcessLogFileHandler(
        IMachineRepository machineRepo,
        IProductionShiftRepository shiftRepo,
        ILogIngestionRepository ingestionRepo,
        IProductionEventRepository eventRepo,
        IDowntimeReasonRepository downtimeRepo,
        IProductionLogParser parser,
        IUnitOfWork uow)
    {
        _machineRepo = machineRepo;
        _shiftRepo = shiftRepo;
        _ingestionRepo = ingestionRepo;
        _eventRepo = eventRepo;
        _downtimeRepo = downtimeRepo;
        _parser = parser;
        _uow = uow;
    }

    public async Task<LogUploadResponse> HandleAsync(ProcessLogFileCommand cmd, CancellationToken ct)
    {
        var machine = await _machineRepo.GetByCodeAsync(cmd.MachineCode, ct)
            ?? throw new MachineNotFoundDomainException(cmd.MachineCode.Value);

        if (cmd.Window.Duration.TotalSeconds >
            machine.PlannedShiftDuration.Seconds + GraceSeconds)
        {
            throw new InvalidWindowDomainException(
                $"Window span ({cmd.Window.Duration.TotalSeconds}s) exceeds " +
                $"planned shift duration + 1h grace ({machine.PlannedShiftDuration.Seconds + GraceSeconds}s).");
        }

        var existing = await _shiftRepo.FindActiveByMachineAndWindowAsync(cmd.MachineCode, cmd.Window, ct);
        if (existing is not null)
            throw new DuplicateShiftDomainException(existing.Id);

        if (machine.PlannedShiftDuration.Seconds <= 0)
            throw new PlannedTimeZeroDomainException();

        var reasons = await _downtimeRepo.ListActiveAsync(ct);
        var reasonsByCode = reasons.ToDictionary(r => r.Code);

        var ingestion = LogIngestion.CreatePending(cmd.MachineCode, cmd.OriginalFileName, cmd.RecordedAt);

        int acceptedCount = 0;
        int rejectedCount = 0;
        int totalParts = 0;
        int approvedParts = 0;
        var rejectedSample = new List<RejectedRow>(MaxRejectedSample);
        var downtimeSeconds = new Dictionary<DowntimeReasonCode, double>();

        void RecordRejection(RejectedRow row)
        {
            rejectedCount++;
            if (rejectedSample.Count < MaxRejectedSample)
                rejectedSample.Add(row);
        }

        await _uow.BeginTransactionAsync(ct);
        try
        {
            ingestion.MarkProcessing();
            await _ingestionRepo.AddAsync(ingestion, ct);
            await _uow.SaveChangesAsync(ct);

            var batch = new List<ProductionEvent>(BatchSize);
            DateTimeOffset? activeDowntimeStart = null;
            DowntimeReasonCode? activeDowntimeCode = null;

            await foreach (var evt in _parser.ParseAsync(cmd.FileStream, RecordRejection, ct))
            {
                if (evt.MachineCode != cmd.MachineCode)
                {
                    // FIX 1: usa evt.RawLine
                    RecordRejection(new RejectedRow(
                        evt.SourceLineNumber, evt.RawLine, RejectedReasons.MachineCodeMismatch));
                    continue;
                }

                var isErrOk = string.Equals(evt.ErrorCode, SentinelNoError, StringComparison.OrdinalIgnoreCase);
                var isScrap = string.Equals(evt.ErrorCode, SentinelScrap, StringComparison.OrdinalIgnoreCase);

                DowntimeReasonCode? downtimeCode = null;
                if (!string.IsNullOrWhiteSpace(evt.ErrorCode) && !isErrOk && !isScrap)
                {
                    var code = new DowntimeReasonCode(evt.ErrorCode!);
                    if (!reasonsByCode.ContainsKey(code))
                    {
                        // FIX 2: usa evt.RawLine
                        RecordRejection(new RejectedRow(
                            evt.SourceLineNumber, evt.RawLine, RejectedReasons.UnknownErrorCode));
                        continue;
                    }
                    downtimeCode = code;
                }

                if (activeDowntimeCode is not null && downtimeCode != activeDowntimeCode)
                {
                    var gapSeconds = (evt.Timestamp - activeDowntimeStart!.Value).TotalSeconds;
                    if (gapSeconds > 0)
                    {
                        downtimeSeconds[activeDowntimeCode] =
                            downtimeSeconds.GetValueOrDefault(activeDowntimeCode) + gapSeconds;
                    }
                    activeDowntimeCode = null;
                    activeDowntimeStart = null;
                }

                if (downtimeCode is not null && activeDowntimeCode is null)
                {
                    activeDowntimeCode = downtimeCode;
                    activeDowntimeStart = evt.Timestamp;
                }

                acceptedCount++;
                totalParts += evt.Quantity;
                if (!isScrap)
                    approvedParts += evt.Quantity;

                batch.Add(evt);
                if (batch.Count >= BatchSize)
                {
                    await _eventRepo.BulkInsertAsync(ingestion.Id, batch, ct);
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
                await _eventRepo.BulkInsertAsync(ingestion.Id, batch, ct);

            if (activeDowntimeCode is not null)
            {
                var gapSeconds = (cmd.Window.End - activeDowntimeStart!.Value).TotalSeconds;
                if (gapSeconds > 0)
                {
                    downtimeSeconds[activeDowntimeCode] =
                        downtimeSeconds.GetValueOrDefault(activeDowntimeCode) + gapSeconds;
                }
            }

            var breakdown = new DowntimeBreakdown(
                downtimeSeconds.ToDictionary(kvp => kvp.Key, kvp => new Duration(kvp.Value)));

            if (breakdown.Total.Seconds > machine.PlannedShiftDuration.Seconds)
                throw new DowntimeOverflowDomainException(machine.PlannedShiftDuration, breakdown.Total);

            var totals = new ProductionTotals(totalParts, approvedParts);
            var metrics = OeeCalculatorEngine.Calculate(
                machine.PlannedShiftDuration,
                breakdown.Total,
                totals,
                machine.IdealCycleTime);

            var shift = ProductionShift.Create(
                cmd.MachineCode,
                cmd.Window,
                totals,
                breakdown,
                metrics,
                ingestion.Id,
                DateTimeOffset.UtcNow);

            await _shiftRepo.AddAsync(shift, ct);

            ingestion.MarkCompleted(
                rowsProcessed: acceptedCount + rejectedCount,
                rowsRejected: rejectedCount,
                rejectedSample: rejectedSample,
                completedAt: DateTimeOffset.UtcNow);

            await _uow.SaveChangesAsync(ct);
            await _uow.CommitAsync(ct);

            return LogUploadResponse.FromDomain(ingestion, shift);
        }
        catch (DomainException ex)
        {
            await RollbackAndPersistFailureAsync(ingestion, MapFailureReason(ex), ct);
            throw;
        }
        catch (OperationCanceledException)
        {
            await RollbackAndPersistFailureAsync(ingestion, "CANCELLED_BY_CLIENT", ct);
            throw;
        }
        catch (Exception)
        {
            await RollbackAndPersistFailureAsync(ingestion, "UNHANDLED", ct);
            throw;
        }
    }

    private async Task RollbackAndPersistFailureAsync(
        LogIngestion ingestion, string reason, CancellationToken ct)
    {
        try { await _uow.RollbackAsync(ct); } catch { }

        try
        {
            ingestion.MarkFailed(reason);
            await _ingestionRepo.PersistFailedAsync(ingestion, ct);
        }
        catch { }
    }

    private static string MapFailureReason(DomainException ex) => ex switch
    {
        PlannedTimeZeroDomainException => "PLANNED_TIME_ZERO",
        DowntimeOverflowDomainException => "DOWNTIME_OVERFLOW",
        _ => "DOMAIN_ERROR"
    };
}