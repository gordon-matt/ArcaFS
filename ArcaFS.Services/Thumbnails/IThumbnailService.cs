namespace ArcaFS.Services.Thumbnails;

/// <summary>
/// Generates and caches image thumbnails for the file listing.
/// </summary>
public interface IThumbnailService
{
    /// <summary>
    /// Returns thumbnail bytes for an image, generating and caching them on first request.
    /// Returns NotFound for anything that isn't a readable image, which the UI treats as
    /// "fall back to the file-type icon".
    /// </summary>
    /// <param name="sourceId">Source that contains the file.</param>
    /// <param name="path">File path as the caller sees it.</param>
    /// <param name="size">Longest-edge size to generate. Defaults to 256.</param>
    /// <param name="principal">
    /// Explicit principal for a public share visit. <see langword="null"/> resolves the current user.
    /// </param>
    /// <param name="cancellationToken">Cancels the permission check and thumbnail generation.</param>
    /// <returns>The thumbnail, or not found when the file is not a readable image.</returns>
    Task<Result<ThumbnailDto>> GetAsync(
        int sourceId,
        string path,
        int size = 256,
        AccessPrincipal? principal = null,
        CancellationToken cancellationToken = default);

    /// <summary>Removes cached thumbnails whose source file is gone. Called by a nightly job.</summary>
    /// <param name="cancellationToken">Cancels the sweep.</param>
    /// <returns>How many cached files were removed.</returns>
    Task<int> PruneAsync(CancellationToken cancellationToken = default);
}
