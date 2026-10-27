using System.Collections.Concurrent;

namespace ArcaFS.Services.Indexing;

/// <summary>
/// Tracks which sources are being scanned right now so the admin page can show progress and the
/// indexer can refuse to run two scans over the same source at once.
/// </summary>
/// <remarks>
/// Deliberately process-local rather than stored in the database. It describes what this
/// process is doing at this instant, and a row left behind by a crashed process would read as a
/// scan that never finishes. Hangfire's own locking is what prevents two servers duplicating
/// work; this is the in-process half.
/// </remarks>
public static class IndexScanState
{
    private static readonly ConcurrentDictionary<int, ScanProgress> Active = new();

    public static bool IsScanning(int sourceId) => Active.ContainsKey(sourceId);

    public static IReadOnlyDictionary<int, ScanProgress> Snapshot => Active;

    /// <summary>
    /// Marks a source as scanning. Returns null if a scan is already running, in which case the
    /// caller must not start another.
    /// </summary>
    public static ScanLease? TryBegin(int sourceId)
    {
        var progress = new ScanProgress();

        return Active.TryAdd(sourceId, progress)
            ? new ScanLease(sourceId, progress)
            : null;
    }

    public static ScanProgress? GetProgress(int sourceId) =>
        Active.TryGetValue(sourceId, out var progress) ? progress : null;

    private static void End(int sourceId) => Active.TryRemove(sourceId, out _);

    /// <summary>Counters a running scan updates, read by the admin page while it works.</summary>
    public sealed class ScanProgress
    {
        private long itemsSeen;
        private long bytesSeen;

        public DateTime StartedAtUtc { get; } = DateTime.UtcNow;

        public long ItemsSeen => Interlocked.Read(ref itemsSeen);

        public long BytesSeen => Interlocked.Read(ref bytesSeen);

        public string? CurrentPath { get; internal set; }

        internal void Add(long items, long bytes)
        {
            Interlocked.Add(ref itemsSeen, items);
            Interlocked.Add(ref bytesSeen, bytes);
        }
    }

    /// <summary>
    /// Releases the source when disposed, so an exception mid-scan can't leave it permanently
    /// marked as scanning.
    /// </summary>
    public sealed class ScanLease(int sourceId, ScanProgress progress) : IDisposable
    {
        public ScanProgress Progress => progress;

        public void Report(long items, long bytes, string? currentPath = null)
        {
            progress.Add(items, bytes);

            if (currentPath is not null)
            {
                progress.CurrentPath = currentPath;
            }
        }

        public void Dispose() => End(sourceId);
    }
}