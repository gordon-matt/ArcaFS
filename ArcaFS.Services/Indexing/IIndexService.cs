namespace ArcaFS.Services.Indexing;

/// <summary>
/// Walks sources into the <c>IndexedItems</c> table. The index makes search, duplicate detection
/// and disk-usage reporting possible without re-walking the filesystem for every query.
/// </summary>
public interface IIndexService
{
    /// <summary>
    /// Scans one source. A second caller while that source is already being scanned gets a
    /// conflict and does not start another walk. A missing root directory is an error and is
    /// recorded as a failed index scan.
    /// </summary>
    /// <param name="sourceId">Source to walk.</param>
    /// <param name="cancellationToken">Cancels the lookup and the walk.</param>
    /// <returns>The scan totals, or a failure when the source is missing, its folder is gone, or a scan is already running.</returns>
    Task<Result<IndexScanResultDto>> ScanSourceAsync(
        int sourceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Scans every enabled, indexed source whose interval has elapsed. A zero interval is
    /// manual only. One source failing does not stop the others. The entry point for the
    /// recurring job.
    /// </summary>
    /// <param name="cancellationToken">Cancels between sources and inside each scan.</param>
    /// <returns>A completed task once every due source has been attempted.</returns>
    Task ScanAllDueAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues a scan on the index queue and returns immediately. Administrator-only. The queued
    /// job does not use this call's cancellation token.
    /// </summary>
    /// <param name="sourceId">Source to scan.</param>
    /// <param name="cancellationToken">Cancels the lookup. Queuing the job is not cancelled once it has started.</param>
    /// <returns>
    /// Success once the job is queued, or a failure when the caller is not an administrator,
    /// the source is missing, or it is already being scanned.
    /// </returns>
    Task<Result> RequestScanAsync(int sourceId, CancellationToken cancellationToken = default);
}
