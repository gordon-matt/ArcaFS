using ArcaFS.Services.Archives;
using ArcaFS.Services.Dashboard;
using ArcaFS.Services.Search;
using ArcaFS.Services.Setup;
using ArcaFS.Services.Shares;
using ArcaFS.Services.Sources;
using ArcaFS.Services.Thumbnails;
using ArcaFS.Services.UserData;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ArcaFS.Services;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers every application service. Authentication services are registered separately by
    /// the host, because which ones apply depends on the configured provider.
    /// </summary>
    public static IServiceCollection AddArcaFsServices(
        this IServiceCollection services,
        IConfiguration configuration,
        string databaseProviderName)
    {
        services.AddMemoryCache();

        services.Configure<ThumbnailOptions>(configuration.GetSection(ThumbnailOptions.SectionName));

        // Default the thumbnail cache under the app's data directory when it isn't configured.
        // Doing it here rather than in the options class keeps path resolution out of a POCO.
        services.PostConfigure<ThumbnailOptions>(options =>
        {
            if (string.IsNullOrWhiteSpace(options.CacheDirectory))
            {
                options.CacheDirectory = Path.Combine(
                    AppContext.BaseDirectory,
                    "App_Data",
                    "thumbnails");
            }

            options.Quality = Math.Clamp(options.Quality, 1, 100);
        });

        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IAccessRuleService, AccessRuleService>();
        services.AddScoped<IAccessGroupService, AccessGroupService>();

        services.AddScoped<IActivityService, ActivityService>();
        services.AddScoped<IServerSettingsService, ServerSettingsService>();
        services.AddScoped<ISourceService, SourceService>();
        services.AddScoped<IFileSystemService, FileSystemService>();
        services.AddScoped<IArchiveService, ArchiveService>();
        services.AddScoped<IIndexService, IndexService>();
        services.AddScoped<ISearchService, SearchService>();
        services.AddScoped<IShareService, ShareService>();
        services.AddScoped<IThumbnailService, ThumbnailService>();
        services.AddScoped<IFavoriteService, FavoriteService>();
        services.AddScoped<IDashboardService, DashboardService>();

        // The setup service needs the provider name for its diagnostic panel, which is a host
        // concern rather than something discoverable from configuration inside the service.
        services.AddScoped<ISetupService>(provider => new SetupService(
            provider.GetRequiredService<ILogger<SetupService>>(),
            provider,
            provider.GetRequiredService<IAuthProviderService>(),
            provider.GetRequiredService<IServerSettingsService>(),
            provider.GetRequiredService<IRepository<Source>>(),
            provider.GetRequiredService<IRepository<AccessRule>>(),
            provider.GetRequiredService<IUserContextService>(),
            databaseProviderName));

        return services;
    }
}