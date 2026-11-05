using OEESentinel.Application.Commands;
using OEESentinel.Application.Dtos;

namespace OEESentinel.Application.Ports.Inbound;

public interface IProcessLogFileHandler
{
    Task<LogUploadResponse> HandleAsync(ProcessLogFileCommand command, CancellationToken ct);
}