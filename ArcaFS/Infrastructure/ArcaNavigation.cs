using ArcaFS.Services.UserData;
using Heroicsoft.NetForge.Abstractions.Navigation;

namespace ArcaFS.Infrastructure;

/// <summary>
/// The sidebar as data for the library shell: fixed links plus the sources and favourites the
/// signed-in user can reach. Scoped to the circuit, so one user's lists never reach another.
/// </summary>
public sealed class ArcaNavigation(
    IPermissionService permissionService,
    IFavoriteService favoriteService,
    ILogger<ArcaNavigation> logger) : IShellNavigation
{
    public async Task<IReadOnlyList<ForgeNavSection>> GetSectionsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SourceSummaryDto> sources = [];
        IReadOnlyList<FavoriteDto> favorites = [];

        try
        {
            sources = await permissionService.GetAccessibleSourcesAsync(cancellationToken);
            var favoritesResult = await favoriteService.ListAsync(cancellationToken);
            favorites = favoritesResult.IsSuccess ? favoritesResult.Value : [];
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The sidebar sources and favourites could not be loaded");
        }

        return BuildSections(sources, favorites);
    }

    private static IReadOnlyList<ForgeNavSection> BuildSections(
        IReadOnlyList<SourceSummaryDto> sources,
        IReadOnlyList<FavoriteDto> favorites)
    {
        List<ForgeNavSection> sections =
        [
            new()
            {
                Items =
                [
                    new() { Title = "Home", Href = "/", Icon = "home", MatchAll = true },
                    new() { Title = "Search", Href = "/search", Icon = "search" },
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
                        Icon = source.IsReadOnly ? "shield" : "library",
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
                        Icon = favorite.IsDirectory ? "pin" : "pin-off",
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
                new() { Title = "Sources", Href = "/admin/sources", Icon = "library" },
                new() { Title = "Access rules", Href = "/admin/access", Icon = "shield" },
                new() { Title = "Users", Href = "/admin/users", Icon = "user" },
                new() { Title = "Groups", Href = "/admin/groups", Icon = "signpost" },
                new() { Title = "Shares", Href = "/admin/shares", Icon = "link" },
                new() { Title = "Activity", Href = "/admin/activity", Icon = "info" },
                new() { Title = "Settings", Href = "/admin/settings", Icon = "settings" }
            ],
        });

        return sections;
    }

    private static string BuildFilesUrl(int sourceId, string path) =>
        $"/files/{sourceId}{(path == "/" ? string.Empty : path)}";
}