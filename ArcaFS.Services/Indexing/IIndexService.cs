namespace ArcaFS.Services.Indexing;

/// <summary>
/// Walks sources into the <c>IndexedItems</c> table. The index makes search, duplicate detection
/// and disk-usage reporting possible without re-walking the filesystem for every query.
/// </summary>
public interface IIndexService
{
    /// <summary>
    /// Scans one source. Safe to call concurrently: the second caller sees the source is already
    /// being scanned and returns without doing anything.
    /// </summary>
    Task<Result<IndexScanResultDto>> ScanSourceAsync(
        int sourceId,
        CancellationToken cancellationToken = default);

    /// <summary>Scans every enabled, indexed source. The entry point for the recurring job.</summary>
    Task ScanAllDueAsync(CancellationToken cancellationToken = default);

    /// <summary>Queues a scan on the index queue and returns immediately. Administrator-only.</summary>
    Task<Result> RequestScanAsync(int sourceId, CancellationToken cancellationToken = default);
}