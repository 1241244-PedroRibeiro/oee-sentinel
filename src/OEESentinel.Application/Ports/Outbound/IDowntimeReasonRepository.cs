using OEESentinel.Domain.Entities;
using OEESentinel.Domain.ValueObjects;

namespace OEESentinel.Application.Ports.Outbound;

public interface IDowntimeReasonRepository
{
    Task<IReadOnlyList<DowntimeReason>> ListActiveAsync(CancellationToken ct);
    Task<DowntimeReason?> GetByCodeAsync(DowntimeReasonCode code, CancellationToken ct);
}