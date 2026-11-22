using Hangfire;

namespace ArcaFS.Services.Indexing;

/// <inheritdoc cref="IIndexService" />
public sealed class IndexService(
    ILogger<IndexService> logger,
    IUserContextService userContext,
    IActivityService activityService,
    IRepository<Source> sourceRepository,
    IRepository<IndexedItem> indexRepository,
    IBackgroundJobClient backgroundJobs) : IIndexService
{
    /// <summary>
    /// Rows written per database round trip. Large enough to keep the walk from being
    /// round-trip bound, small enough that a scan of a million files doesn't hold the whole
    /// index in memory or produce a parameter list SQL Server rejects.
    /// </summary>
    private const int BatchSize = 500;

    /// <inheritdoc />
    public async Task<Result> RequestScanAsync(int sourceId, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var source = await sourceRepository.FindOneAsync(new SearchOptions<Source>
        {
            Query = s => s.Id == sourceId,
            CancellationToken = cancellationToken,
        });

        if (source is null)
        {
            return Result.NotFound($"Source {sourceId} was not found.");
        }

        if (IndexScanState.IsScanning(sourceId))
        {
            return Result.Conflict($"'{source.Name}' is already being scanned.");
        }

        backgroundJobs.Enqueue<IIndexService>(
            Constants.HangfireQueues.Index,
            service => service.ScanSourceAsync(sourceId, CancellationToken.None));

        logger.LogInformation(
            "Scan of source {SourceId} '{Name}' queued by {User}",
            sourceId,
            source.Name,
            userContext.GetCurrentUserName());

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task ScanAllDueAsync(CancellationToken cancellationToken = default)
    {
        var sources = await sourceRepository.FindAsync(new SearchOptions<Source>
        {
            Query = s => !s.IsDisabled && s.IsIndexed,
            CancellationToken = cancellationToken,
        });

        var now = DateTime.UtcNow;

        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A zero interval means "manual only" — the source is still indexed, just never on
            // a schedule.
            if (source.IndexIntervalMinutes <= 0)
            {
                continue;
            }

            bool isDue = source.LastIndexedAt is null
                || source.LastIndexedAt.Value.AddMinutes(source.IndexIntervalMinutes) <= now;

            if (!isDue || IndexScanState.IsScanning(source.Id))
            {
                continue;
            }

            var result = await ScanSourceAsync(source.Id, cancellationToken);

            if (!result.IsSuccess)
            {
                // One bad source shouldn't stop the others from being indexed.
                logger.LogWarning(
                    "Scan of source {SourceId} '{Name}' did not complete: {Reason}",
                    source.Id,
                    source.Name,
                    result.GetDisplayMessage());
            }
        }
    }

    /// <inheritdoc />
    public async Task<Result<IndexScanResultDto>> ScanSourceAsync(
        int sourceId,
        CancellationToken cancellationToken = default)
    {
        var source = await sourceRepository.FindOneAsync(new SearchOptions<Source>
        {
            Query = s => s.Id == sourceId,
            CancellationToken = cancellationToken,
        });

        if (source is null)
        {
            return Result<IndexScanResultDto>.NotFound($"Source {sourceId} was not found.");
        }

        if (!Directory.Exists(source.Path))
        {
            logger.LogWarning(
                "Source {SourceId} '{Name}' points at {Path}, which does not exist",
                source.Id,
                source.Name,
                source.Path);

            await activityService.RecordAsync(
                ActivityKind.IndexScan,
                sourceId,
                Constants.RootPath,
                succeeded: false,
                detail: $"'{source.Path}' does not exist.",
                cancellationToken: cancellationToken);

            return Result<IndexScanResultDto>.Error($"'{source.Path}' does not exist.");
        }

        using var lease = IndexScanState.TryBegin(sourceId);
        if (lease is null)
        {
            return Result<IndexScanResultDto>.Conflict($"'{source.Name}' is already being scanned.");
        }

        var startedAt = DateTime.UtcNow;
        logger.LogInformation("Scanning source {SourceId} '{Name}'", source.Id, source.Name);

        var existing = await LoadExistingAsync(sourceId, cancellationToken);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var excludes = ExcludeMatcher.For(source);

        var pendingInserts = new List<IndexedItem>(BatchSize);
        var pendingUpdates = new List<IndexedItem>(BatchSize);

        long fileCount = 0;
        long directoryCount = 0;
        long totalBytes = 0;

        try
        {
            // Walked depth-first by hand rather than with RecurseSubdirectories so directory
            // sizes and child counts can be accumulated on the way back up.
            var (Files, Directories, Bytes) = await WalkAsync(
                source,
                source.Path,
                Constants.RootPath,
                depth: 0,
                excludes,
                existing,
                seen,
                pendingInserts,
                pendingUpdates,
                lease,
                cancellationToken);

            fileCount = Files;
            directoryCount = Directories;
            totalBytes = Bytes;

            await FlushAsync(pendingInserts, pendingUpdates, cancellationToken);

            // Anything not touched by this pass is gone from disk.
            var removed = existing.Values.Where(i => !seen.Contains(i.Path)).ToList();
            if (removed.Count > 0)
            {
                await indexRepository.DeleteAsync(removed);
            }

            source.LastIndexedAt = DateTime.UtcNow;
            source.IndexedItemCount = fileCount + directoryCount;
            source.IndexedSizeBytes = totalBytes;
            await sourceRepository.UpdateAsync(source);

            var elapsed = DateTime.UtcNow - startedAt;

            logger.LogInformation(
                "Indexed source {SourceId} '{Name}': {Files} file(s), {Directories} folder(s), " +
                "{Size} in {Elapsed:0.0}s ({Removed} row(s) removed)",
                source.Id,
                source.Name,
                fileCount,
                directoryCount,
                FileKinds.FormatSize(totalBytes),
                elapsed.TotalSeconds,
                removed.Count);

            return Result<IndexScanResultDto>.Success(new IndexScanResultDto(
                sourceId,
                fileCount,
                directoryCount,
                totalBytes,
                removed.Count,
                elapsed));
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Scan of source {SourceId} was cancelled", sourceId);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Scan of source {SourceId} '{Name}' failed", source.Id, source.Name);

            await activityService.RecordAsync(
                ActivityKind.IndexScan,
                sourceId,
                Constants.RootPath,
                succeeded: false,
                detail: ex.Message,
                cancellationToken: cancellationToken);

            return Result<IndexScanResultDto>.Error("The scan failed. See the log for details.");
        }
    }

    /// <summary>
    /// Indexes one directory and everything under it, returning its recursive totals so the
    /// parent can record a directory size without a second pass.
    /// </summary>
    private async Task<(long Files, long Directories, long Bytes)> WalkAsync(
        Source source,
        string physicalPath,
        string virtualPath,
        int depth,
        ExcludeMatcher excludes,
        Dictionary<string, IndexedItem> existing,
        HashSet<string> seen,
        List<IndexedItem> pendingInserts,
        List<IndexedItem> pendingUpdates,
        IndexScanState.ScanLease lease,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        long files = 0;
        long directories = 0;
        long bytes = 0;
        int childCount = 0;

        FileSystemInfo[] entries;

        try
        {
            entries = new DirectoryInfo(physicalPath).GetFileSystemInfos();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable subdirectory is common on a real filesystem. Skip it and keep going
            // rather than abandoning the whole scan.
            logger.LogDebug(ex, "Skipped unreadable directory {Physical}", physicalPath);
            return (0, 0, 0);
        }

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entry.Name.EndsWith(Constants.UploadTempSuffix, StringComparison.Ordinal)
                || excludes.IsExcluded(entry.Name))
            {
                continue;
            }

            if (!source.FollowSymlinks && entry.LinkTarget is not null)
            {
                continue;
            }

            string childVirtual = VirtualPath.Combine(virtualPath, entry.Name);
            childCount++;

            if (entry is DirectoryInfo)
            {
                var (Files, Directories, Bytes) = await WalkAsync(
                    source,
                    entry.FullName,
                    childVirtual,
                    depth + 1,
                    excludes,
                    existing,
                    seen,
                    pendingInserts,
                    pendingUpdates,
                    lease,
                    cancellationToken);

                files += Files;
                directories += Directories + 1;
                bytes += Bytes;
            }
            else if (entry is FileInfo file)
            {
                Record(
                    source.Id,
                    childVirtual,
                    virtualPath,
                    file,
                    isDirectory: false,
                    size: file.Length,
                    depth: depth + 1,
                    childCount: 0,
                    existing,
                    seen,
                    pendingInserts,
                    pendingUpdates);

                files++;
                bytes += file.Length;
            }

            if (pendingInserts.Count >= BatchSize || pendingUpdates.Count >= BatchSize)
            {
                await FlushAsync(pendingInserts, pendingUpdates, cancellationToken);
            }
        }

        // The root itself isn't indexed; it has no name and nothing links to it.
        if (depth > 0)
        {
            Record(
                source.Id,
                virtualPath,
                VirtualPath.GetParent(virtualPath) ?? Constants.RootPath,
                new DirectoryInfo(physicalPath),
                isDirectory: true,
                size: bytes,
                depth: depth,
                childCount: childCount,
                existing,
                seen,
                pendingInserts,
                pendingUpdates);
        }

        lease.Report(childCount, bytes, virtualPath);

        return (files, directories, bytes);
    }

    /// <summary>
    /// Adds or refreshes one row. An unchanged file is left alone entirely, which is what keeps
    /// a repeat scan of a large, static source cheap.
    /// </summary>
    private static void Record(
        int sourceId,
        string virtualPath,
        string parentPath,
        FileSystemInfo info,
        bool isDirectory,
        long size,
        int depth,
        int childCount,
        Dictionary<string, IndexedItem> existing,
        HashSet<string> seen,
        List<IndexedItem> pendingInserts,
        List<IndexedItem> pendingUpdates)
    {
        seen.Add(virtualPath);

        var kind = FileKinds.Classify(info.Name, isDirectory);
        var modified = info.LastWriteTimeUtc;
        bool isHidden = info.Name.StartsWith('.') || info.Attributes.HasFlag(FileAttributes.Hidden);

        if (existing.TryGetValue(virtualPath, out var row))
        {
            bool unchanged = row.SizeBytes == size
                && row.ModifiedAt == modified
                && row.ChildCount == childCount
                && row.IsDirectory == isDirectory
                && row.IsHidden == isHidden;

            if (unchanged)
            {
                return;
            }

            row.SizeBytes = size;
            row.ModifiedAt = modified;
            row.ChildCount = childCount;
            row.IsDirectory = isDirectory;
            row.IsHidden = isHidden;
            row.Kind = kind;
            row.LastSeenAt = DateTime.UtcNow;

            // The bytes changed, so any hash recorded for it is now wrong.
            row.ContentHash = null;

            pendingUpdates.Add(row);

            return;
        }

        pendingInserts.Add(new IndexedItem
        {
            SourceId = sourceId,
            Path = virtualPath,
            ParentPath = parentPath,
            Name = info.Name,
            NameLower = info.Name.ToLowerInvariant(),
            Extension = isDirectory ? null : info.Extension,
            IsDirectory = isDirectory,
            IsHidden = isHidden,
            SizeBytes = size,
            ModifiedAt = modified,
            Kind = kind,
            Depth = depth,
            ChildCount = childCount,
            LastSeenAt = DateTime.UtcNow,
        });
    }

    private async Task FlushAsync(
        List<IndexedItem> pendingInserts,
        List<IndexedItem> pendingUpdates,
        CancellationToken cancellationToken)
    {
        if (pendingInserts.Count > 0)
        {
            await indexRepository.InsertAsync(pendingInserts);
            pendingInserts.Clear();
        }

        if (pendingUpdates.Count > 0)
        {
            await indexRepository.UpdateAsync(pendingUpdates);
            pendingUpdates.Clear();
        }
    }

    private async Task<Dictionary<string, IndexedItem>> LoadExistingAsync(
        int sourceId,
        CancellationToken cancellationToken)
    {
        var rows = await indexRepository.FindAsync(new SearchOptions<IndexedItem>
        {
            Query = i => i.SourceId == sourceId,
            CancellationToken = cancellationToken,
        });

        // Loaded up front so the walk can tell new from changed from unchanged without a query
        // per entry. Costs roughly 200 bytes per indexed item, which is an acceptable trade
        // against one round trip per file.
        return rows.ToDictionary(i => i.Path, StringComparer.OrdinalIgnoreCase);
    }
}