namespace ArcaFS.Services.UserData;

/// <summary>Paths a user has pinned to the sidebar for quick access.</summary>
public interface IFavoriteService
{
    /// <summary>
    /// The caller's favourites that they can still browse or read. A pin whose access was
    /// revoked is omitted. No signed-in user yields an empty list.
    /// </summary>
    /// <param name="cancellationToken">Cancels the lookup and the permission checks.</param>
    /// <returns>The visible favourites.</returns>
    Task<Result<IReadOnlyList<FavoriteDto>>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Pins a path the caller can browse. The stored path is the scoped path, so a scoped
    /// user's pin keeps matching what they see.
    /// </summary>
    /// <param name="sourceId">Source that contains the path.</param>
    /// <param name="path">Path as the caller sees it.</param>
    /// <param name="cancellationToken">Cancels the permission check and the insert.</param>
    /// <returns>
    /// The new favourite, or a failure when there is no signed-in user, the path cannot be
    /// browsed, or it is already pinned.
    /// </returns>
    Task<Result<FavoriteDto>> AddAsync(
        int sourceId,
        string path,
        CancellationToken cancellationToken = default);

    /// <summary>Removes one of the caller's favourites. An administrator cannot remove someone else's.</summary>
    /// <param name="favoriteId">Favourite id.</param>
    /// <param name="cancellationToken">Cancels the lookup. The delete is not cancelled once it has started.</param>
    /// <returns>Success, or a failure when there is no signed-in user, the pin is missing, or it belongs to someone else.</returns>
    Task<Result> RemoveAsync(int favoriteId, CancellationToken cancellationToken = default);

    /// <summary>Adds or removes in one call, for the star button in the toolbar.</summary>
    /// <param name="sourceId">Source that contains the path.</param>
    /// <param name="path">Path as the caller sees it. Compared after normalization.</param>
    /// <param name="cancellationToken">Cancels the lookup and, when adding, the permission check.</param>
    /// <returns>
    /// <see langword="true"/> when the path is now pinned, <see langword="false"/> when the pin
    /// was removed, or a failure when there is no signed-in user or the path cannot be browsed.
    /// </returns>
    Task<Result<bool>> ToggleAsync(
        int sourceId,
        string path,
        CancellationToken cancellationToken = default);
}
