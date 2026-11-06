using OEESentinel.Application.Ports.Outbound;
using OEESentinel.Domain.Entities;
using OEESentinel.Domain.ValueObjects;

namespace OEESentinel.UnitTests.Fakes;

public sealed class FakeDowntimeReasonRepository : IDowntimeReasonRepository
{
    private readonly Dictionary<string, DowntimeReason> _reasons = new();

    public void Add(DowntimeReason reason) => _reasons[reason.Code.Value] = reason;

    public Task<IReadOnlyList<DowntimeReason>> ListActiveAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DowntimeReason>>(
            _reasons.Values.Where(r => r.IsActive).ToList());

    public Task<DowntimeReason?> GetByCodeAsync(DowntimeReasonCode code, CancellationToken ct) =>
        Task.FromResult(_reasons.GetValueOrDefault(code.Value));
}