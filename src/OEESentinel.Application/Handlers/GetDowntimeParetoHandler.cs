using OEESentinel.Application.Commands;
using OEESentinel.Application.Dtos;
using OEESentinel.Application.Ports.Inbound;
using OEESentinel.Application.Ports.Outbound;
using OEESentinel.Domain.Exceptions;
using OEESentinel.Domain.ValueObjects;

namespace OEESentinel.Application.Handlers;

public sealed class GetDowntimeParetoHandler : IGetDowntimeParetoHandler
{
    private readonly IProductionShiftRepository _shiftRepo;
    private readonly IMachineRepository _machineRepo;
    private readonly IDowntimeReasonRepository _reasonRepo;

    public GetDowntimeParetoHandler(
        IProductionShiftRepository shiftRepo,
        IMachineRepository machineRepo,
        IDowntimeReasonRepository reasonRepo)
    {
        _shiftRepo = shiftRepo;
        _machineRepo = machineRepo;
        _reasonRepo = reasonRepo;
    }

    public async Task<DowntimeParetoDto> HandleAsync(GetDowntimeParetoQuery q, CancellationToken ct)
    {
        if (q.From >= q.To)
            throw new InvalidWindowDomainException("from must be strictly before to.");

        if ((q.To - q.From).TotalDays > 90)
            throw new InvalidWindowDomainException("Window span must be ≤ 90 days.");

        _ = await _machineRepo.GetByCodeAsync(q.MachineCode, ct)
            ?? throw new MachineNotFoundDomainException(q.MachineCode.Value);

        var shifts = await _shiftRepo.ListActiveByDateRangeAsync(q.MachineCode, q.From, q.To, ct);
        if (shifts.Count == 0)
            return new DowntimeParetoDto(q.MachineCode.Value, new WindowDto(q.From, q.To), 0, 0, Array.Empty<ParetoEntryDto>());

        var reasons = await _reasonRepo.ListActiveAsync(ct);
        var reasonsByCode = reasons.ToDictionary(r => r.Code.Value);

        var aggregate = new Dictionary<string, double>();
        foreach (var shift in shifts)
        {
            foreach (var (code, duration) in shift.Downtime.Entries)
            {
                aggregate[code.Value] = aggregate.GetValueOrDefault(code.Value) + duration.Seconds;
            }
        }

        var total = aggregate.Values.Sum();
        if (total <= 0)
            return new DowntimeParetoDto(q.MachineCode.Value, new WindowDto(q.From, q.To), shifts.Count, 0, Array.Empty<ParetoEntryDto>());

        var entries = aggregate
            .OrderByDescending(kvp => kvp.Value)
            .ThenBy(kvp => kvp.Key, StringComparer.Ordinal)
            .Take(20)
            .Select(kvp => new ParetoEntryDto(
                ReasonCode: kvp.Key,
                Category: reasonsByCode.TryGetValue(kvp.Key, out var r) ? r.Category.ToString() : "UNPLANNED",
                Description: reasonsByCode.TryGetValue(kvp.Key, out var r2) ? r2.Description : kvp.Key,
                Seconds: kvp.Value,
                Percentage: Math.Round(kvp.Value / total, 4)))
            .ToList();

        return new DowntimeParetoDto(
            q.MachineCode.Value,
            new WindowDto(q.From, q.To),
            shifts.Count,
            Math.Round(total, 2),
            entries);
    }
}