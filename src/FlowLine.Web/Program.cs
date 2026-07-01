using FlowLine.Application.Builder;
using FlowLine.Application.Orders;
using FlowLine.Application.Relay;
using FlowLine.Application.Stations;
using FlowLine.Application.Timing;
using FlowLine.Infrastructure.Data;
using FlowLine.Web.Components;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContext<FlowLineDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("FlowLine")));

// Singleton: must outlive any one circuit so a hand-off on one browser's circuit can
// reach a station screen on a different browser's circuit (PRD FR-16/NFR-9).
builder.Services.AddSingleton<IRelayNotifier, RelayNotifier>();
builder.Services.AddScoped<IRelayService, RelayService>();

// Uploaded media (FR-4) lives under wwwroot/media so app.UseStaticFiles() below serves it
// directly at /media/... with no separate file-serving endpoint.
var mediaRootPath = Path.Combine(builder.Environment.WebRootPath, "media");
Directory.CreateDirectory(mediaRootPath);
builder.Services.AddSingleton(new MediaStorageOptions { RootPath = mediaRootPath });
builder.Services.AddScoped<IWorkflowBuilderService, WorkflowBuilderService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<ITimingService, TimingService>();
builder.Services.AddScoped<IStationService, StationService>();

var app = builder.Build();

// Self-contained deployment (NFR-1): the app brings its own schema up to date
// and seeds the demo workflow on startup, so a shop-floor PC never needs the
// dotnet-ef tool installed to run `database update` manually.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FlowLineDbContext>();
    db.Database.Migrate();
    await SeedData.SeedAsync(db);
}

// Plain HTTP on the LAN by design (PRD §9.1: stations browse to http://flowline:5000);
// no internet-facing HTTPS/HSTS needed for a single shop-floor network.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
