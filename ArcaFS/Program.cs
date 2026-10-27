using ArcaFS.Components;
using Hangfire;
using Heroicsoft.NetForge.Abstractions.Data;
using LogVue;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.HttpOverrides;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLogVue(options =>
    builder.Configuration.GetSection(LogVueOptions.SectionName).Bind(options));

builder.Services.AddSerilog((services, configuration) => configuration
    .MinimumLevel.Information()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.LogVue(services));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Synology (and many nginx) reverse proxies close idle HTTP requests at 60s. SignalR's
// long-poll default is 90s, so when WebSockets are blocked the proxy returns an HTML
// error page and the circuit dies. Finish the poll first; the client starts another.
builder.Services.Configure<HttpConnectionDispatcherOptions>(options =>
    options.LongPolling.PollTimeout = TimeSpan.FromSeconds(20));

builder.Services.AddControllers();
builder.Services.AddRazorPages();
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

// Registered after authentication because several services depend on IUserContextService, and
// which implementation that is depends on the auth mode.
builder.Services.AddArcaFsServices(builder.Configuration, databaseProvider);

builder.Services.AddArcaFsHangfire(builder.Configuration, databaseProvider);

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

var app = builder.Build();

app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}

// Behind a reverse proxy the scheme and client address arrive in headers. Applied before
// HTTPS redirection so the redirect doesn't fire on an already-secure request. The defaults
// only trust loopback; a NAS or Docker proxy sits on another address, so those headers would
// otherwise be ignored and Blazor's WebSocket would try ws:// behind an https:// URL.
var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
};
forwardedHeaders.KnownIPNetworks.Clear();
forwardedHeaders.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeaders);

app.UseHttpsRedirection();
app.UseStaticFiles();
app.MapStaticAssets();

app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();

app.MapLogVue().RequireAuthorization(new AuthorizeAttribute
{
    Roles = WellKnownRoles.Administrator
});

app.UseNetForgeSetupGate();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new AdministratorDashboardAuthorizationFilter()],
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .WithStaticAssets();

app.MapControllers();

if (authProvider == AuthProvider.Identity)
{
    // Identity's built-in login and account pages are Razor Pages, so they are only mapped when
    // that provider is active.
    app.MapRazorPages().WithStaticAssets();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

await using (var scope = app.Services.CreateAsyncScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        await DbInitializer.InitializeAsync(
            scope.ServiceProvider,
            authProvider,
            app.Configuration,
            logger);
    }
    catch (Exception ex)
    {
        // Starting with an un-migrated database would fail confusingly on the first request
        // instead of here, so this is deliberately fatal.
        logger.LogError(ex, "Database initialization failed");
        throw;
    }
}

HangfireRecurringJobs.Register();

Log.Information(
    "ArcaFS started with the {DatabaseProvider} database provider and {AuthProvider} authentication",
    databaseProvider,
    authProvider);

app.Run();