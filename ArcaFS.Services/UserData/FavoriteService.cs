namespace ArcaFS.Services.UserData;

public sealed class FavoriteService(
    ILogger<FavoriteService> logger,
    IUserContextService userContext,
    IPermissionService permissionService,
    IRepository<FavoriteItem> repository) : IFavoriteService
{
    public async Task<Result<IReadOnlyList<FavoriteDto>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        string? userId = userContext.GetCurrentUserId();
        if (userId is null)
        {
            return Result<IReadOnlyList<FavoriteDto>>.Success([]);
        }

        var favorites = await repository.FindAsync(new SearchOptions<FavoriteItem>
        {
            Query = f => f.UserId == userId,
            Include = q => q.Include(f => f.Source),
            OrderBy = q => q.OrderBy(f => f.SortOrder).ThenBy(f => f.Path),
            CancellationToken = cancellationToken,
        });

        var visible = new List<FavoriteDto>();

        foreach (var favorite in favorites)
        {
            // A favourite can outlive the access that created it: the rule granting it may have
            // been removed, or the folder deleted. Silently hiding it is better than a sidebar
            // full of links that error on click.
            var permissions = await permissionService.GetPermissionsAsync(
                favorite.SourceId,
                favorite.Path,
                cancellationToken);

            if (!permissions.HasAny(FilePermission.Browse | FilePermission.Read))
            {
                continue;
            }

            visible.Add(new FavoriteDto(
                favorite.Id,
                favorite.SourceId,
                favorite.Source?.Name ?? string.Empty,
                favorite.Path,
                favorite.Label ?? ResolveLabel(favorite),
                favorite.IsDirectory));
        }

        return Result<IReadOnlyList<FavoriteDto>>.Success(visible);
    }

    public async Task<Result<FavoriteDto>> AddAsync(
        int sourceId,
        string path,
        CancellationToken cancellationToken = default)
    {
        string? userId = userContext.GetCurrentUserId();
        if (userId is null)
        {
            return Result<FavoriteDto>.Unauthorized();
        }

        var resolved = await permissionService.RequireAsync(
            sourceId,
            path,
            FilePermission.Browse,
            cancellationToken);

        if (!resolved.IsSuccess)
        {
            return resolved.To<FavoriteDto>();
        }

        var target = resolved.Value;

        var existing = await repository.FindOneAsync(new SearchOptions<FavoriteItem>
        {
            Query = f => f.UserId == userId
                && f.SourceId == sourceId
                && f.Path == target.ScopedVirtualPath,
            CancellationToken = cancellationToken,
        });

        if (existing is not null)
        {
            return Result<FavoriteDto>.Conflict("That is already a favourite.");
        }

        int nextOrder = await repository.CountAsync(f => f.UserId == userId);

        var created = await repository.InsertAsync(new FavoriteItem
        {
            UserId = userId,
            SourceId = sourceId,

            // Stored scoped, so a scoped user's favourite keeps working and an admin looking at
            // the row sees the same path the user does.
            Path = target.ScopedVirtualPath,
            IsDirectory = Directory.Exists(target.PhysicalPath),
            SortOrder = nextOrder,
        });

        return Result<FavoriteDto>.Success(new FavoriteDto(
            created.Id,
            sourceId,
            target.Source.Name,
            created.Path,
            ResolveLabel(created),
            created.IsDirectory));
    }

    public async Task<Result> RemoveAsync(int favoriteId, CancellationToken cancellationToken = default)
    {
        string? userId = userContext.GetCurrentUserId();
        if (userId is null)
        {
            return Result.Unauthorized();
        }

        var favorite = await repository.FindOneAsync(new SearchOptions<FavoriteItem>
        {
            Query = f => f.Id == favoriteId,
            CancellationToken = cancellationToken,
        });

        if (favorite is null)
        {
            return Result.NotFound();
        }

        // Favourites are personal; not even an administrator removes someone else's.
        if (!string.Equals(favorite.UserId, userId, StringComparison.Ordinal))
        {
            return Result.Forbidden();
        }

        await repository.DeleteAsync(favorite);

        return Result.Success();
    }

    public async Task<Result<bool>> ToggleAsync(
        int sourceId,
        string path,
        CancellationToken cancellationToken = default)
    {
        string? userId = userContext.GetCurrentUserId();
        if (userId is null)
        {
            return Result<bool>.Unauthorized();
        }

        string normalized = VirtualPath.Normalize(path);

        var existing = await repository.FindOneAsync(new SearchOptions<FavoriteItem>
        {
            Query = f => f.UserId == userId && f.SourceId == sourceId && f.Path == normalized,
            CancellationToken = cancellationToken,
        });

        if (existing is not null)
        {
            await repository.DeleteAsync(existing);
            return Result<bool>.Success(false);
        }

        var added = await AddAsync(sourceId, path, cancellationToken);

        return added.IsSuccess
            ? Result<bool>.Success(true)
            : added.To<bool>();
    }

    /// <summary>
    /// Falls back to the folder name, or the source name for a favourite on the source root
    /// (whose path has no last segment).
    /// </summary>
    private static string ResolveLabel(FavoriteItem favorite)
    {
        string name = VirtualPath.GetName(favorite.Path);

        return name.Length > 0
            ? name
            : favorite.Source?.Name ?? favorite.Path;
    }
}