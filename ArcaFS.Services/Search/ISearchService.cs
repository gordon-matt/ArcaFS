namespace ArcaFS.Services.Search;

/// <summary>
/// Searches the index, and the two tools built on top of it: the duplicate finder and the
/// disk-usage breakdown. All three filter their results through the caller's access rules, so
/// searching is never a way to discover a path you could not browse to.
/// </summary>
public interface ISearchService
{
    Task<Result<SearchResultsDto>> SearchAsync(
        SearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Groups files by content hash. Hashes are computed on demand for size-matched candidates
    /// only, since hashing an entire source would be prohibitive.
    /// </summary>
    Task<Result<IReadOnlyList<DuplicateGroupDto>>> FindDuplicatesAsync(
        int sourceId,
        string path,
        long minimumSizeBytes = 1024,
        CancellationToken cancellationToken = default);

    /// <summary>Immediate children of a folder with their recursive sizes, largest first.</summary>
    Task<Result<DiskUsageDto>> GetDiskUsageAsync(
        int sourceId,
        string path,
        CancellationToken cancellationToken = default);
}