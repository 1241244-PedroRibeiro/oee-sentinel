using Microsoft.EntityFrameworkCore;
using OEESentinel.Application.Handlers;
using OEESentinel.Application.Ports.Inbound;
using OEESentinel.Application.Ports.Outbound;
using OEESentinel.Infrastructure.Parsing;
using OEESentinel.Infrastructure.Persistence;
using OEESentinel.Infrastructure.Persistence.Repositories;
using OEESentinel.Infrastructure.Persistence.Seeding;
using OEESentinel.WebUI.Components;

using Microsoft.AspNetCore.Mvc;
using OEESentinel.Application.Commands;
using OEESentinel.Application.Dtos;
using OEESentinel.Domain.Exceptions;
using OEESentinel.Domain.ValueObjects;
using OEESentinel.WebUI.Errors;
using System.Globalization;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddOpenApi();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<OEESentinelDbContext>(options =>
    options.UseNpgsql(connectionString));

// Outbound ports
builder.Services.AddScoped<IMachineRepository, EfMachineRepository>();
builder.Services.AddScoped<IProductionShiftRepository, EfProductionShiftRepository>();
builder.Services.AddScoped<ILogIngestionRepository, EfLogIngestionRepository>();
builder.Services.AddScoped<IProductionEventRepository, EfProductionEventRepository>();
builder.Services.AddScoped<IDowntimeReasonRepository, EfDowntimeReasonRepository>();
builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork>();
builder.Services.AddScoped<IProductionLogParser, CsvProductionLogParser>();

// Inbound handlers
builder.Services.AddScoped<IListMachinesHandler, ListMachinesHandler>();
builder.Services.AddScoped<IProcessLogFileHandler, ProcessLogFileHandler>();

builder.Services.AddScoped<IGetLogIngestionHandler, GetLogIngestionHandler>();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddScoped<IGetMachineShiftHandler, GetMachineShiftHandler>();
builder.Services.AddScoped<IGetMachineOeeHandler, GetMachineOeeHandler>();
builder.Services.AddScoped<IGetDowntimeParetoHandler, GetDowntimeParetoHandler>();
builder.Services.AddScoped<IListRecentIngestionsHandler, ListRecentIngestionsHandler>();

var app = builder.Build();

if (app.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("SeedData:Enabled"))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<OEESentinelDbContext>();
    await dbContext.Database.MigrateAsync();
    await BaselineSeed.SeedAsync(dbContext);
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// ── MACH-01 ──────────────────────────────────────────────────────────────
app.MapGet("/api/v1/machines", async (IListMachinesHandler handler, CancellationToken ct) =>
{
    var machines = await handler.HandleAsync(new ListMachinesQuery(), ct);
    return Results.Ok(machines);
})
.WithName("ListMachines");

// ── LOG-01: upload ───────────────────────────────────────────────────────
app.MapPost("/api/v1/logs", async (
    [FromForm] IFormFile file,
    [FromForm] string machineCode,
    [FromForm] string windowStart,
    [FromForm] string windowEnd,
    IProcessLogFileHandler handler,
    CancellationToken ct) =>
{
    if (file is null || file.Length == 0)
        return Results.Problem(
            title: "Missing file",
            detail: "The 'file' field is required and must be non-empty.",
            statusCode: StatusCodes.Status400BadRequest);

    MachineCode code;
    try { code = new MachineCode(machineCode); }
    catch (ArgumentException ex)
    {
        return Results.Problem(
            title: "Invalid machineCode",
            detail: ex.Message,
            statusCode: StatusCodes.Status400BadRequest,
            type: "https://oee-sentinel.local/errors/invalid-machine-code");
    }

    if (!DateTimeOffset.TryParse(
            windowStart,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var start))
    {
        return Results.Problem(
            title: "Invalid windowStart",
            detail: "windowStart must be ISO 8601 UTC (e.g., 2026-11-04T08:00:00Z).",
            statusCode: StatusCodes.Status400BadRequest,
            type: "https://oee-sentinel.local/errors/invalid-window");
    }

    if (!DateTimeOffset.TryParse(
            windowEnd,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var end))
    {
        return Results.Problem(
            title: "Invalid windowEnd",
            detail: "windowEnd must be ISO 8601 UTC (e.g., 2026-11-04T16:00:00Z).",
            statusCode: StatusCodes.Status400BadRequest,
            type: "https://oee-sentinel.local/errors/invalid-window");
    }

    TimeRange window;
    try { window = new TimeRange(start, end); }
    catch (ArgumentException ex)
    {
        return Results.Problem(
            title: "Invalid window",
            detail: ex.Message,
            statusCode: StatusCodes.Status400BadRequest,
            type: "https://oee-sentinel.local/errors/invalid-window");
    }

    using var stream = file.OpenReadStream();
    var cmd = new ProcessLogFileCommand(
        code, window, file.FileName, stream, DateTimeOffset.UtcNow);

    var result = await handler.HandleAsync(cmd, ct);

    return Results.Created(
        $"/api/v1/machines/{result.MachineCode}/shifts/{result.ProductionShiftId}",
        result);
})
.DisableAntiforgery()
.WithName("UploadLog");

// ── LOG-02: status ───────────────────────────────────────────────────────
app.MapGet("/api/v1/logs/{ingestionId:guid}", async (
    Guid ingestionId,
    IGetLogIngestionHandler handler,
    CancellationToken ct) =>
{
    var dto = await handler.HandleAsync(ingestionId, ct);
    return dto is null
        ? Results.Problem(
            title: "Ingestion not found",
            detail: $"Log ingestion '{ingestionId}' does not exist.",
            statusCode: StatusCodes.Status404NotFound,
            type: "https://oee-sentinel.local/errors/ingestion-not-found")
        : Results.Ok(dto);
})
.WithName("GetLogIngestion");

// ── OBS-01: health ───────────────────────────────────────────────────────
var startedAt = DateTimeOffset.UtcNow;
app.MapGet("/api/v1/health", async (
    OEESentinelDbContext db,
    CancellationToken ct) =>
{
    var healthy = true;
    try
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        await db.Database.ExecuteSqlRawAsync("SELECT 1", timeout.Token);
    }
    catch
    {
        healthy = false;
    }

    var body = new
    {
        status = healthy ? "ok" : "degraded",
        version = "0.1.0-baseline",
        timestamp = DateTimeOffset.UtcNow,
        database = healthy ? "connected" : "unreachable",
        uptimeSeconds = (long)(DateTimeOffset.UtcNow - startedAt).TotalSeconds
    };

    return healthy
        ? Results.Ok(body)
        : Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
})
.WithName("Health");

// ── MACH-01 single ───────────────────────────────────────────────────────
app.MapGet("/api/v1/machines/{code}", async (
    string code,
    IMachineRepository repo,
    CancellationToken ct) =>
{
    MachineCode machineCode;
    try { machineCode = new MachineCode(code); }
    catch (ArgumentException ex)
    {
        return Results.Problem(title: "Invalid machineCode", detail: ex.Message, statusCode: 400);
    }

    var m = await repo.GetByCodeAsync(machineCode, ct);
    return m is null
        ? Results.Problem(
            title: "Machine not found",
            detail: $"Machine '{code}' does not exist.",
            statusCode: 404,
            type: "https://oee-sentinel.local/errors/machine-not-found")
        : Results.Ok(new MachineDto(m.Id, m.Code.Value, m.Name, m.IdealCycleTime.Seconds, m.PlannedShiftDuration.Seconds, m.IsActive));
});

// ── OEE-01: single shift ────────────────────────────────────────────────
app.MapGet("/api/v1/machines/{code}/shifts/{shiftId:guid}", async (
    string code,
    Guid shiftId,
    IGetMachineShiftHandler handler,
    CancellationToken ct) =>
{
    var dto = await handler.HandleAsync(new GetMachineShiftQuery(shiftId), ct);
    return Results.Ok(dto);
});

// ── OEE-01: list shifts in window ───────────────────────────────────────
app.MapGet("/api/v1/machines/{code}/shifts", async (
    string code,
    DateTimeOffset from,
    DateTimeOffset to,
    IProductionShiftRepository repo,
    CancellationToken ct) =>
{
    if (from >= to)
        return Results.Problem(title: "Invalid window", statusCode: 400, type: "https://oee-sentinel.local/errors/invalid-window");

    var list = await repo.ListActiveByDateRangeAsync(new MachineCode(code), from, to, ct);
    var emptyCategories = new Dictionary<string, string>();
    return Results.Ok(list.Select(s => ProductionShiftSummaryDto.FromDomain(s, emptyCategories)).ToList());
});

// ── OEE-02: aggregated OEE ──────────────────────────────────────────────
app.MapGet("/api/v1/machines/{code}/oee", async (
    string code,
    DateTimeOffset from,
    DateTimeOffset to,
    IGetMachineOeeHandler handler,
    CancellationToken ct) =>
{
    var dto = await handler.HandleAsync(new GetMachineOeeQuery(new MachineCode(code), from, to), ct);
    return Results.Ok(dto);
});

// ── OEE-03: pareto ──────────────────────────────────────────────────────
app.MapGet("/api/v1/machines/{code}/pareto", async (
    string code,
    DateTimeOffset from,
    DateTimeOffset to,
    IGetDowntimeParetoHandler handler,
    CancellationToken ct) =>
{
    var dto = await handler.HandleAsync(new GetDowntimeParetoQuery(new MachineCode(code), from, to), ct);
    return Results.Ok(dto);
});

// ── LOG-02: recent ingestions ───────────────────────────────────────────
app.MapGet("/api/v1/logs", async (
    int? limit,
    IListRecentIngestionsHandler handler,
    CancellationToken ct) =>
{
    var list = await handler.HandleAsync(new ListRecentIngestionsQuery(limit ?? 20), ct);
    return Results.Ok(list);
});

app.Run();