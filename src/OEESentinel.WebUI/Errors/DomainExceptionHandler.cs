using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using OEESentinel.Domain.Exceptions;

namespace OEESentinel.WebUI.Errors;

public sealed class DomainExceptionHandler : IExceptionHandler
{
    private readonly ILogger<DomainExceptionHandler> _logger;

    public DomainExceptionHandler(ILogger<DomainExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is not DomainException domain)
            return false;

        var (status, type, title) = domain switch
        {
            MachineNotFoundDomainException =>
                (StatusCodes.Status404NotFound, "machine-not-found", "Machine not found"),
            LogIngestionNotFoundDomainException =>
                (StatusCodes.Status404NotFound, "ingestion-not-found", "Ingestion not found"),
            InvalidWindowDomainException =>
                (StatusCodes.Status400BadRequest, "invalid-window", "Invalid window"),
            DuplicateShiftDomainException =>
                (StatusCodes.Status409Conflict, "shift-already-exists", "Shift already exists"),
            PlannedTimeZeroDomainException =>
                (StatusCodes.Status422UnprocessableEntity, "planned-time-zero", "Planned time is zero"),
            DowntimeOverflowDomainException =>
                (StatusCodes.Status422UnprocessableEntity, "downtime-overflow", "Downtime exceeds planned time"),
            _ =>
                (StatusCodes.Status422UnprocessableEntity, "domain-error", "Domain error")
        };

        _logger.LogInformation(
            "Domain exception {Type} on {Path}: {Message}",
            exception.GetType().Name, context.Request.Path, exception.Message);

        var problem = new ProblemDetails
        {
            Type = $"https://oee-sentinel.local/errors/{type}",
            Title = title,
            Status = status,
            Detail = exception.Message,
            Instance = context.Request.Path
        };

        if (domain is DuplicateShiftDomainException dup)
            problem.Extensions["existingShiftId"] = dup.ExistingShiftId;

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(problem, cancellationToken: ct);
        return true;
    }
}