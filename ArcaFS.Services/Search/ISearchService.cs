namespace ArcaFS.Services.Search;

/// <summary>
/// Searches the index, and the two tools built on top of it: the duplicate finder and the
/// disk-usage breakdown. All three filter their results through the caller's access rules, so
/// searching is never a way to discover a path you could not browse to.
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// Searches the index of sources the caller can browse. An empty query returns no rows.
    /// At most 5,000 candidates are read before access filtering and paging.
    /// </summary>
    /// <param name="request">Query text, optional source and folder, and page.</param>
    /// <param name="cancellationToken">Cancels the lookup and the access checks.</param>
    /// <returns>
    /// The page. The result can carry a warning when the candidate cap was hit, or when an
    /// indexed source in the search has never completed a scan.
    /// </returns>
    Task<Result<SearchResultsDto>> SearchAsync(
        SearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Groups files by content hash. Hashes are computed on demand for size-matched candidates
    /// only, since hashing an entire source would be prohibitive.
    /// </summary>
    /// <param name="sourceId">Source to search.</param>
    /// <param name="path">Folder to search under, as the caller sees it.</param>
    /// <param name="minimumSizeBytes">Files smaller than this are ignored. Defaults to 1024.</param>
    /// <param name="cancellationToken">Cancels the lookup and the hashing.</param>
    /// <returns>
    /// Groups of two or more identical files, or a failure when the caller cannot browse and read that folder.
    /// </returns>
    Task<Result<IReadOnlyList<DuplicateGroupDto>>> FindDuplicatesAsync(
        int sourceId,
        string path,
        long minimumSizeBytes = 1024,
        CancellationToken cancellationToken = default);

    /// <summary>Immediate children of a folder with their recursive sizes, largest first.</summary>
    /// <param name="sourceId">Source that contains the folder.</param>
    /// <param name="path">Folder path as the caller sees it.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The children and the folder total, or a failure when the caller cannot browse it.</returns>
    Task<Result<DiskUsageDto>> GetDiskUsageAsync(
        int sourceId,
        string path,
        CancellationToken cancellationToken = default);
}
