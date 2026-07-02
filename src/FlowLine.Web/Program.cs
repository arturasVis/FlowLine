using System.Security.Claims;
using FlowLine.Application.Assignments;
using FlowLine.Application.Builder;
using FlowLine.Application.Line;
using FlowLine.Application.Orders;
using FlowLine.Application.Relay;
using FlowLine.Application.Staff;
using FlowLine.Application.Stations;
using FlowLine.Application.Stats;
using FlowLine.Application.Timing;
using FlowLine.Infrastructure.Data;
using FlowLine.Web.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Auth: cookie-based, staff-code-only login (PRD §4 allowed a stubbed auth; this is the
// explicit richer version the company asked for). Level comes from StaffTable."TestingPower".
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        // A signed-in user direct-loading a page above their level is redirected here by
        // endpoint auth (Routes.razor's NotAuthorized only catches in-app navigations) —
        // bouncing them to the login form they already passed would just be confusing.
        options.AccessDeniedPath = "/denied";
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(FlowLinePolicies.CanViewReports, p => p.RequireAssertion(ctx =>
        int.TryParse(ctx.User.FindFirst(FlowLineClaims.Level)?.Value, out var l) && l >= AccessLevel.Advanced));
    options.AddPolicy(FlowLinePolicies.CanManage, p => p.RequireAssertion(ctx =>
        int.TryParse(ctx.User.FindFirst(FlowLineClaims.Level)?.Value, out var l) && l >= AccessLevel.Manager));
});

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
builder.Services.AddScoped<IStatsService, StatsService>();
builder.Services.AddScoped<ILineStatusService, LineStatusService>();
builder.Services.AddScoped<IStationService, StationService>();
builder.Services.AddScoped<IStaffService, StaffService>();
builder.Services.AddScoped<IAssignmentService, AssignmentService>();

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
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Login/logout run OUTSIDE the Blazor circuit: a live SignalR circuit can't set the auth
// cookie (the HTTP response is already flushed), so the static login form (Login.razor) posts
// here. Antiforgery is disabled on these two anonymous endpoints to keep the form a plain POST.
app.MapPost("/auth/login", async (HttpContext http, IStaffService staff) =>
{
    var form = await http.Request.ReadFormAsync();
    var member = await staff.GetByCodeAsync(form["code"].ToString());
    if (member is null)
    {
        return Results.Redirect("/login?error=1");
    }

    var level = AccessLevel.Normalize(member.TestingPower);
    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, member.StaffNumber.ToString()),
        new(ClaimTypes.Name, member.Name),
        new(FlowLineClaims.Level, level.ToString()),
    };
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.Redirect("/");
}).AllowAnonymous().DisableAntiforgery();

app.MapPost("/auth/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).DisableAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
