using ArcaFS.Data.MySql;
using ArcaFS.Data.Npgsql;
using ArcaFS.Data.Sql;
using ArcaFS.Data.Sqlite;
using Hangfire;
using Heroicsoft.NetForge.Abstractions.Data;
using Heroicsoft.NetForge.Abstractions.Setup;

namespace ArcaFS.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Delegates to the provider project matching <paramref name="provider"/> (already validated by
    /// <see cref="DatabaseProviders.Resolve"/>), so a mistyped value fails at startup instead of
    /// silently opening SQLite.
    /// </summary>
    public static IServiceCollection AddArcaFsDatabase(
        this IServiceCollection services,
        IConfiguration configuration,
        string provider) => provider switch
        {
            DatabaseProviders.Sqlite => services.AddArcaFSSqlite(configuration),
            DatabaseProviders.SqlServer => services.AddArcaFSSqlServer(configuration),
            DatabaseProviders.Npgsql => services.AddArcaFSNpgsql(configuration),
            DatabaseProviders.MySql => services.AddArcaFSMySql(configuration),
            _ => throw new InvalidOperationException($"Unknown {DatabaseProviders.ConfigurationKey} '{provider}'."),
        };

    /// <summary>
    /// Registers the NetForge shell for ArcaFS: theme and shell options, the sidebar data, and the
    /// first-run gate. Shared by the web host and the desktop host.
    /// </summary>
    /// <remarks>
    /// Scoped to the circuit, so one user's toasts and sidebar refreshes stay theirs. The scheme
    /// is light, dark, or the operating system, stored in this browser.
    /// </remarks>
    public static IServiceCollection AddArcaFsShell(this IServiceCollection services)
    {
        services.AddNetForgeComponents(ui =>
        {
            ui.ThemeStorageKey = Constants.ClientThemeLocalStorageKey;
            ui.DefaultColorScheme = "system";
            ui.AccentColor = "#0f766e";
            ui.AccentColorDark = "#5eead4";
        });

        services.Configure<ForgeShellOptions>(shell =>
        {
            shell.Brand = "ArcaFS";
            shell.LogoUrl = "img/Logo.png";
            // The wordmark's dark letters disappear on a dark bar, so dark themes get the icon.
            shell.LogoDarkUrl = "img/Icon.png";
            shell.MarkUrl = "img/Icon.png";
            shell.SearchUrlFormat = "/search?q={0}";
            shell.SearchPlaceholder = "Search files";
            shell.UserMenuItems =
            [
                new() { Title = "Profile", Href = "/profile", Icon = "user" },
                new() { Title = "My shares", Href = "/shares", Icon = "link" },
            ];
        });

        services.AddScoped<IShellNavigation, ArcaNavigation>();

        services.AddScoped<ISetupStatus, ServerSetupStatus>();
        services.AddNetForgeSetupGate();

        return services;
    }

    public static IServiceCollection AddArcaFsRepositories(this IServiceCollection services)
    {
        // Extenso maps the open generic IRepository<> to its EF implementation; no scanning.
        services.AddEntityFrameworkRepository();

        return services;
    }

    public static IServiceCollection AddArcaFsHangfire(
        this IServiceCollection services,
        IConfiguration configuration,
        string provider)
    {
        // Hangfire gets its own connection string when one is supplied, so an operator can keep
        // job state off the application database.
        string connectionString = configuration.GetConnectionString("Hangfire")
            is { Length: > 0 } dedicated
            ? dedicated
            : configuration.GetConnectionString("DefaultConnection") ?? string.Empty;

        switch (provider)
        {
            case DatabaseProviders.Sqlite:
                services.AddArcaFSSqliteHangfire(configuration["Hangfire:SqlitePath"]);
                break;

            case DatabaseProviders.SqlServer:
                services.AddArcaFSSqlServerHangfire(connectionString);
                break;

            case DatabaseProviders.Npgsql:
                services.AddArcaFSNpgsqlHangfire(connectionString);
                break;

            case DatabaseProviders.MySql:
                services.AddArcaFSMySqlHangfire(connectionString);
                break;

            default:
                throw new InvalidOperationException($"Unknown Database:Provider '{provider}'.");
        }

        // Separate servers per queue so a long index scan can't starve interactive work.
        services.AddHangfireServer(options =>
        {
            options.ServerName = "main";
            options.Queues =
            [
                Constants.HangfireQueues.Default,
                Constants.HangfireQueues.Critical,
            ];
        });

        // One worker for indexing: two concurrent scans of the same disk are slower than one,
        // and IndexScanState already refuses to scan a source twice at once.
        services.AddHangfireServer(options =>
        {
            options.ServerName = "index";
            options.Queues = [Constants.HangfireQueues.Index];
            options.WorkerCount = 1;
        });

        services.AddHangfireServer(options =>
        {
            options.ServerName = "thumbnails";
            options.Queues = [Constants.HangfireQueues.Thumbnails];
            options.WorkerCount = Math.Max(1, Environment.ProcessorCount / 2);
        });

        return services;
    }

    /// <summary>
    /// Wires up whichever authentication provider is configured and returns which one it was,
    /// so <c>Program.cs</c> can decide whether to map Identity's Razor Pages and what the seeder should do.
    /// </summary>
    public static AuthProvider AddArcaFsAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var provider = services.AddNetForgeAuthentication<ApplicationUser, ApplicationRole, ApplicationDbContextBase>(
            configuration);

        switch (provider)
        {
            case AuthProvider.Identity:
                services.AddScoped<IUserInfoService, AspNetIdentityUserInfoService>();
                services.AddScoped<IAdminUserManagementService, AdminIdentityUserManagementService>();
                break;

            case AuthProvider.Keycloak:
                services.AddScoped<IUserInfoService, KeycloakUserInfoService>();
                break;

            case AuthProvider.None:
                services.AddSingleton<IUserInfoService, NoneUserInfoService>();
                break;
        }

        return provider;
    }
}