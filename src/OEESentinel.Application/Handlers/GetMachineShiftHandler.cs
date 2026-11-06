using OEESentinel.Application.Commands;
using OEESentinel.Application.Dtos;
using OEESentinel.Application.Ports.Inbound;
using OEESentinel.Application.Ports.Outbound;
using OEESentinel.Domain.Exceptions;

namespace OEESentinel.Application.Handlers;

public sealed class GetMachineShiftHandler : IGetMachineShiftHandler
{
    private readonly IProductionShiftRepository _shiftRepo;
    private readonly IDowntimeReasonRepository _reasonRepo;

    public GetMachineShiftHandler(
        IProductionShiftRepository shiftRepo,
        IDowntimeReasonRepository reasonRepo)
    {
        _shiftRepo = shiftRepo;
        _reasonRepo = reasonRepo;
    }

    public async Task<ProductionShiftSummaryDto> HandleAsync(GetMachineShiftQuery q, CancellationToken ct)
    {
        var shift = await _shiftRepo.GetByIdAsync(q.ShiftId, ct)
            ?? throw new InvalidWindowDomainException($"Shift '{q.ShiftId}' not found.");

        var reasons = await _reasonRepo.ListActiveAsync(ct);
        var categoryByCode = reasons.ToDictionary(r => r.Code.Value, r => r.Category.ToString());

        return ProductionShiftSummaryDto.FromDomain(shift, categoryByCode);
    }
}