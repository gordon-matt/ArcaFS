namespace ArcaFS.Services.Thumbnails;

/// <summary>
/// Generates and caches image thumbnails for the grid and gallery views.
/// </summary>
public interface IThumbnailService
{
    /// <summary>
    /// Returns thumbnail bytes for an image, generating and caching them on first request.
    /// Returns NotFound for anything that isn't a readable image, which the UI treats as
    /// "fall back to the file-type icon".
    /// </summary>
    Task<Result<ThumbnailDto>> GetAsync(
        int sourceId,
        string path,
        int size = 256,
        AccessPrincipal? principal = null,
        CancellationToken cancellationToken = default);

    /// <summary>Removes cached thumbnails whose source file is gone. Called by a nightly job.</summary>
    Task<int> PruneAsync(CancellationToken cancellationToken = default);
}
