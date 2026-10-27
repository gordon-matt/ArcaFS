namespace ArcaFS.Services.UserData;

/// <summary>Paths a user has pinned to the sidebar for quick access.</summary>
public interface IFavoriteService
{
    Task<Result<IReadOnlyList<FavoriteDto>>> ListAsync(CancellationToken cancellationToken = default);

    Task<Result<FavoriteDto>> AddAsync(
        int sourceId,
        string path,
        CancellationToken cancellationToken = default);

    Task<Result> RemoveAsync(int favoriteId, CancellationToken cancellationToken = default);

    /// <summary>Adds or removes in one call, for the star button in the toolbar.</summary>
    Task<Result<bool>> ToggleAsync(
        int sourceId,
        string path,
        CancellationToken cancellationToken = default);
}