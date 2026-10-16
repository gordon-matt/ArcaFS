using ArcaFS.Services.UserData;
using Heroicsoft.NetForge.Abstractions.Navigation;

namespace ArcaFS.Infrastructure;

/// <summary>
/// The sidebar as data for the library <c>ShellLayout</c>: fixed links plus the sources and
/// favourites the signed-in user can reach.
/// </summary>
/// <remarks>
/// <see cref="IShellNavigation.Sections"/> is synchronous but the lists come from the database,
/// so they load in the background and are announced through <see cref="SidebarRefresh"/>, which
/// makes the layout re-read <see cref="Sections"/>. Pages call
/// <see cref="SidebarRefresh.NotifyChanged"/> after changing a source or a favourite, which
/// triggers another load. Scoped to the circuit, so one user's lists never reach another.
/// </remarks>
public sealed class ArcaNavigation : IShellNavigation, IDisposable
{
    private readonly IPermissionService permissionService;
    private readonly IFavoriteService favoriteService;
    private readonly SidebarRefresh sidebarRefresh;
    private readonly ILogger<ArcaNavigation> logger;
    private readonly CancellationTokenSource disposed = new();

    private IReadOnlyList<SourceSummaryDto> sources = [];
    private IReadOnlyList<FavoriteDto> favorites = [];
    private bool publishing;

    public ArcaNavigation(
        IPermissionService permissionService,
        IFavoriteService favoriteService,
        SidebarRefresh sidebarRefresh,
        ILogger<ArcaNavigation> logger)
    {
        this.permissionService = permissionService;
        this.favoriteService = favoriteService;
        this.sidebarRefresh = sidebarRefresh;
        this.logger = logger;

        // Subscribed before the layout, so a refresh starts the load first.
        sidebarRefresh.Changed += OnSidebarChanged;
        _ = LoadAsync();
    }

    public IReadOnlyList<ForgeNavSection> Sections => BuildSections();

    public void Dispose()
    {
        sidebarRefresh.Changed -= OnSidebarChanged;
        disposed.Cancel();
        disposed.Dispose();
    }

    private IReadOnlyList<ForgeNavSection> BuildSections()
    {
        List<ForgeNavSection> sections =
        [
            new()
            {
                Items =
                [
                    new() { Title = "Home", Href = "/", Icon = "bi-house-door", MatchAll = true },
                    new() { Title = "Search", Href = "/search", Icon = "bi-search" },
                ],
            },
        ];

        if (sources.Count > 0)
        {
            sections.Add(new()
            {
                Heading = "Sources",
                Items =
                [
                    .. sources.Select(source => new ForgeNavItem
                    {
                        Title = source.Name,
                        Href = $"/files/{source.Id}",
                        Icon = source.IsReadOnly ? "bi-lock-fill" : "bi-folder-fill",
                    }),
                ],
            });
        }

        if (favorites.Count > 0)
        {
            sections.Add(new()
            {
                Heading = "Favourites",
                Items =
                [
                    .. favorites.Select(favorite => new ForgeNavItem
                    {
                        Title = favorite.Label,
                        Href = BuildFilesUrl(favorite.SourceId, favorite.Path),
                        Icon = favorite.IsDirectory ? "bi-star-fill" : "bi-star",
                    }),
                ],
            });
        }

        sections.Add(new()
        {
            Heading = "Administration",
            Roles = WellKnownRoles.Administrator,
            Items =
            [
                new() { Title = "Sources", Href = "/admin/sources", Icon = "bi-hdd-stack" },
                new() { Title = "Access rules", Href = "/admin/access", Icon = "bi-shield-lock" },
                new() { Title = "Users", Href = "/admin/users", Icon = "bi-people" },
                new() { Title = "Groups", Href = "/admin/groups", Icon = "bi-diagram-3" },
                new() { Title = "Shares", Href = "/admin/shares", Icon = "bi-link-45deg" },
                new() { Title = "Activity", Href = "/admin/activity", Icon = "bi-clock-history" },
                new() { Title = "Settings", Href = "/admin/settings", Icon = "bi-sliders" },
                // Both are outside the Blazor router, so they open as a full navigation.
                new() { Title = "Jobs", Href = "/hangfire", Icon = "bi-cpu", External = true },
                new() { Title = "Logs", Href = "/logvue", Icon = "bi-journal-text", External = true },
            ],
        });

        return sections;
    }

    private void OnSidebarChanged()
    {
        // Ignore the notification this class raises itself once a load has finished.
        if (!publishing)
        {
            _ = LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        try
        {
            var token = disposed.Token;

            var loadedSources = await permissionService.GetAccessibleSourcesAsync(token);
            var favoritesResult = await favoriteService.ListAsync(token);

            sources = loadedSources;
            favorites = favoritesResult.IsSuccess ? favoritesResult.Value : [];

            publishing = true;
            try
            {
                sidebarRefresh.NotifyChanged();
            }
            finally
            {
                publishing = false;
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // The circuit or request ended before the lists arrived.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The sidebar sources and favourites could not be loaded");
        }
    }

    private static string BuildFilesUrl(int sourceId, string path) =>
        $"/files/{sourceId}{(path == "/" ? string.Empty : path)}";
}
