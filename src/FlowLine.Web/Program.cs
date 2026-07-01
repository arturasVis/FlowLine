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

// Provider is config-driven (NFR-2/NFR-3): SQLite for zero-friction local dev, SQL Server for
// the company deployment. Switching is a config change (DatabaseProvider + connection string),
// not a code edit. Each provider has its own migrations assembly — SQLite's live in
// FlowLine.Infrastructure, SQL Server's in FlowLine.Migrations.SqlServer — because one assembly
// can only hold a single model snapshot per DbContext.
var databaseProvider = builder.Configuration["DatabaseProvider"] ?? "Sqlite";
var connectionString = builder.Configuration.GetConnectionString("FlowLine");
builder.Services.AddDbContext<FlowLineDbContext>(options =>
{
    switch (databaseProvider.ToLowerInvariant())
    {
        case "sqlserver":
            options.UseSqlServer(connectionString,
                sql => sql.MigrationsAssembly("FlowLine.Migrations.SqlServer"));
            break;
        case "sqlite":
            options.UseSqlite(connectionString);
            break;
        default:
            throw new InvalidOperationException(
                $"Unknown DatabaseProvider '{databaseProvider}'. Use 'Sqlite' or 'SqlServer'.");
    }
});

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
// Reads orders from the company History table — only functional on SQL Server, where those
// external tables exist. Registered unconditionally; the import UI guards on the provider.
builder.Services.AddScoped<IOrderImportService, OrderImportService>();
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
