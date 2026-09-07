using ElectronNET.API;
using ElectronNET.API.Entities;
using Hangfire;
using Sejil;
using Serilog;
using ArcaFS.Desktop;
using ArcaFS.Infrastructure;

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
    ["Database:Provider"] = ArcaFS.Constants.DatabaseProviders.Sqlite,
    ["Authentication:Provider"] = ArcaFS.Constants.AuthProviders.None,
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

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(appDataDir, "logs", "arcafs-.log"), rollingInterval: RollingInterval.Day)
    .WriteTo.ArcaFsDatabase(builder.Configuration)
    .CreateLogger();

builder.Host.UseSerilog();
builder.Host.UseSejil(writeToProviders: true);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();

builder.Services.AddScoped<SidebarState>();
builder.Services.AddScoped<ToastService>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<DownloadTicketService>();

string databaseProvider = ArcaFsConfiguration.ResolveDatabaseProvider(builder.Configuration);

builder.Services.AddArcaFsDatabase(builder.Configuration);
builder.Services.AddArcaFsRepositories();

var authProvider = builder.Services.AddArcaFsAuthentication(builder.Configuration);

builder.Services.AddArcaFsServices(builder.Configuration, databaseProvider);

builder.Services.ConfigureSejil(options =>
    options.AuthenticationScheme = NoneAuthenticationHandler.SchemeName);

builder.Services.AddArcaFsHangfire(builder.Configuration);

builder.Services.AddElectron();
builder.UseElectron(args, OnElectronAppReadyAsync);

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseStaticFiles();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

app.UseSejil();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new HangfireAuthorizationFilter()],
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
