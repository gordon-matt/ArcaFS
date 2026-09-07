using ArcaFS.Components;
using ArcaFS.Infrastructure;
using Hangfire;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Sejil;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog is configured before the host is built so startup failures — a bad connection string,
// a missing migration — are logged rather than lost.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.ArcaFsDatabase(builder.Configuration)
    .CreateLogger();

builder.Host.UseSerilog();
builder.Host.UseSejil(writeToProviders: true);

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

// Scoped to the circuit, so one user's notifications and sidebar refreshes stay theirs.
builder.Services.AddScoped<SidebarState>();
builder.Services.AddScoped<ToastService>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<DownloadTicketService>();

string databaseProvider = ArcaFsConfiguration.ResolveDatabaseProvider(builder.Configuration);

builder.Services.AddArcaFsDatabase(builder.Configuration);
builder.Services.AddArcaFsRepositories();

var authProvider = builder.Services.AddArcaFsAuthentication(builder.Configuration);

// Registered after authentication because several services depend on IUserContextService, and
// which implementation that is depends on the auth mode.
builder.Services.AddArcaFsServices(builder.Configuration, databaseProvider);

// Sejil's dashboard has no authorization of its own, so it is pointed at whichever scheme is
// active. The admin-only restriction is applied by the endpoint filter further down.
builder.Services.ConfigureSejil(options => options.AuthenticationScheme = authProvider switch
{
    AuthProvider.Keycloak => CookieAuthenticationDefaults.AuthenticationScheme,
    AuthProvider.Identity => IdentityConstants.ApplicationScheme,
    _ => NoneAuthenticationHandler.SchemeName,
});

builder.Services.AddArcaFsHangfire(builder.Configuration);

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

app.UseSejil();

app.UseMiddleware<SetupRedirectMiddleware>();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new HangfireAuthorizationFilter()],
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
