using ArcaFS.Desktop;
using ArcaFS.Infrastructure;
using ElectronNET.API;
using ElectronNET.API.Entities;
using Hangfire;
using Heroicsoft.NetForge.Abstractions.Data;
using LogVue;
using Microsoft.AspNetCore.Authorization;
using Serilog;

// Desktop entry point. The web/Docker entry point (ArcaFS/Program.cs) stays separate.
//
// Differences from the web build:
//   • Database:Provider is forced to Sqlite.
//   • Authentication:Provider is forced to None (single local administrator, no login UI).
//   • The DB, Hangfire and thumbnail cache live under the per-user app data folder
//     so a non-admin install can write to them.
//   • A default source is created from the user's profile folder when none exists.

string appDataDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "ArcaFS");
Directory.CreateDirectory(appDataDir);

string defaultSourcePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
if (string.IsNullOrWhiteSpace(defaultSourcePath) || !Directory.Exists(defaultSourcePath))
{
    defaultSourcePath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
}

var overrides = new Dictionary<string, string?>
{
    ["Database:Provider"] = DatabaseProviders.Sqlite,
    ["Authentication:Provider"] = AuthProviderNames.None,
    ["ConnectionStrings:DefaultConnection"] = $"Data Source={Path.Combine(appDataDir, "arcafs.db")}",
    ["Hangfire:SqlitePath"] = Path.Combine(appDataDir, "hangfire.db"),
    ["Thumbnails:CacheDirectory"] = Path.Combine(appDataDir, "thumbnails"),
    ["Desktop:DefaultSourcePath"] = defaultSourcePath,
};

string webRootPath = SharedAssetLocator.ResolveWebRoot();

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = webRootPath,
});

builder.Configuration.AddInMemoryCollection(overrides);

Directory.CreateDirectory(Path.Combine(appDataDir, "logs"));

string logFile = Path.Combine(appDataDir, "logs", "arcafs-.log");

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File(logFile, rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Services.AddLogVue(options =>
    builder.Configuration.GetSection(LogVueOptions.SectionName).Bind(options));

builder.Services.AddSerilog((services, configuration) => configuration
    .MinimumLevel.Information()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(logFile, rollingInterval: RollingInterval.Day)
    .WriteTo.LogVue(services));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();

builder.Services.AddArcaFsShell();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<DownloadTicketService>();

string databaseProvider = DatabaseProviders.Resolve(
    builder.Configuration[DatabaseProviders.ConfigurationKey],
    DatabaseProviders.Sqlite,
    [.. DatabaseProviders.All]);

builder.Services.AddArcaFsDatabase(builder.Configuration, databaseProvider);
builder.Services.AddArcaFsRepositories();

var authProvider = builder.Services.AddArcaFsAuthentication(builder.Configuration);

builder.Services.AddArcaFsServices(builder.Configuration, databaseProvider);

builder.Services.AddArcaFsHangfire(builder.Configuration, databaseProvider);

builder.Services.AddElectron();
builder.UseElectron(args, OnElectronAppReadyAsync);

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseStaticFiles();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

app.UseNetForgeSetupGate();

app.MapLogVue().RequireAuthorization(new AuthorizeAttribute
{
    Roles = WellKnownRoles.Administrator
});

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new AdministratorDashboardAuthorizationFilter()],
});

app.MapRazorComponents<ArcaFS.Components.App>()
    .AddInteractiveServerRenderMode();

app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

await using (var scope = app.Services.CreateAsyncScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await DbInitializer.InitializeAsync(scope.ServiceProvider, authProvider, app.Configuration, logger);
}

HangfireRecurringJobs.Register();

Log.Information("ArcaFS desktop started (SQLite + no-auth)");

app.Run();

static async Task OnElectronAppReadyAsync()
{
    var options = new BrowserWindowOptions
    {
        Show = false,
        Width = 1400,
        Height = 900,
        Title = "ArcaFS",
        IsRunningBlazor = true,
    };

    if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
    {
        options.AutoHideMenuBar = true;
    }

    var window = await Electron.WindowManager.CreateWindowAsync(options);
    window.OnReadyToShow += window.Show;
}
