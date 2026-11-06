using OEESentinel.Application.Commands;
using OEESentinel.Application.Dtos;
using OEESentinel.Application.Ports.Inbound;
using OEESentinel.Application.Ports.Outbound;
using OEESentinel.Domain.Exceptions;
using OEESentinel.Domain.ValueObjects;

namespace OEESentinel.Application.Handlers;

public sealed class GetMachineOeeHandler : IGetMachineOeeHandler
{
    private readonly IProductionShiftRepository _shiftRepo;
    private readonly IMachineRepository _machineRepo;

    public GetMachineOeeHandler(
        IProductionShiftRepository shiftRepo,
        IMachineRepository machineRepo)
    {
        _shiftRepo = shiftRepo;
        _machineRepo = machineRepo;
    }

    public async Task<MachineOeeAggregateDto> HandleAsync(GetMachineOeeQuery q, CancellationToken ct)
    {
        if (q.From >= q.To)
            throw new InvalidWindowDomainException("from must be strictly before to.");

        var spanDays = (q.To - q.From).TotalDays;
        if (spanDays > 90)
            throw new InvalidWindowDomainException("Window span must be ≤ 90 days.");

        var machine = await _machineRepo.GetByCodeAsync(q.MachineCode, ct)
            ?? throw new MachineNotFoundDomainException(q.MachineCode.Value);

        var shifts = await _shiftRepo.ListActiveByDateRangeAsync(q.MachineCode, q.From, q.To, ct);

        if (shifts.Count == 0)
            throw new InvalidWindowDomainException("No shifts found in window.");

        // Agregar de totals, NUNCA por média de percentagens (KB §23.2)
        var totalPlanned = shifts.Sum(s => machine.PlannedShiftDuration.Seconds);
        var totalDowntime = shifts.Sum(s => s.Downtime.Total.Seconds);
        var totalParts = shifts.Sum(s => s.Totals.TotalParts);
        var totalApproved = shifts.Sum(s => s.Totals.ApprovedParts);

        var operating = totalPlanned - totalDowntime;
        if (operating <= 0 || totalPlanned <= 0)
            throw new InvalidWindowDomainException("Degenerate window — no operating time.");

        var availability = operating / totalPlanned;
        var theoreticalParts = operating / machine.IdealCycleTime.Seconds;
        var performance = theoreticalParts > 0 ? Math.Min(totalParts / theoreticalParts, 1.0) : 0.0;
        var quality = totalParts > 0 ? (double)totalApproved / totalParts : 1.0;
        var oee = availability * performance * quality;

        return new MachineOeeAggregateDto(
            MachineCode: q.MachineCode.Value,
            Window: new WindowDto(q.From, q.To),
            ShiftsIncluded: shifts.Count,
            Totals: new MachineTotalsDto(
                PlannedSeconds: totalPlanned,
                DowntimeSeconds: totalDowntime,
                OperatingSeconds: operating,
                TotalParts: totalParts,
                ApprovedParts: totalApproved,
                RejectedParts: totalParts - totalApproved),
            Availability: Math.Round(availability, 4),
            Performance: Math.Round(performance, 4),
            Quality: Math.Round(quality, 4),
            Oee: Math.Round(oee, 4),
            LastUpdatedAt: shifts.Max(s => s.RecordedAt));
    }
}