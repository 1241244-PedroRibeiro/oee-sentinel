using Microsoft.EntityFrameworkCore;
using OEESentinel.Application.Handlers;
using OEESentinel.Application.Ports.Outbound;
using OEESentinel.Infrastructure.Persistence;
using OEESentinel.Infrastructure.Persistence.Repositories;
using OEESentinel.Infrastructure.Persistence.Seeding;
using OEESentinel.WebUI.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// 1. EF Core Configuration
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<OEESentinelDbContext>(options =>
    options.UseNpgsql(connectionString));

// 2. Dependency Injection (Ports & Adapters)
builder.Services.AddScoped<IMachineRepository, EfMachineRepository>();
builder.Services.AddScoped<IListMachinesHandler, ListMachinesHandler>();

var app = builder.Build();

// 3. Database Migration & Seeding (Apenas em Development)
if (app.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("SeedData:Enabled"))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<OEESentinelDbContext>();
    await dbContext.Database.MigrateAsync();
    await BaselineSeed.SeedAsync(dbContext);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// 4. REST API Endpoint (MACH-01)
app.MapGet("/api/v1/machines", async (IListMachinesHandler handler, CancellationToken ct) =>
{
    var machines = await handler.HandleAsync(new ListMachinesQuery(), ct);
    return Results.Ok(machines);
})
.WithName("ListMachines");

app.Run();