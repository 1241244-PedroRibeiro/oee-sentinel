using OEESentinel.Application.Commands;
using OEESentinel.Application.Dtos;

namespace OEESentinel.Application.Ports.Inbound;

public interface IGetMachineShiftHandler
{
    Task<ProductionShiftSummaryDto> HandleAsync(GetMachineShiftQuery q, CancellationToken ct);
}