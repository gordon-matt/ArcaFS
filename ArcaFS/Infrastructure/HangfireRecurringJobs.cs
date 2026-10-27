using ArcaFS.Services.Thumbnails;
using Hangfire;

namespace ArcaFS.Infrastructure;

/// <summary>
/// Registers the scheduled work. Called once at startup, after the database is ready.
/// </summary>
public static class HangfireRecurringJobs
{
    public static void Register()
    {
        // Runs every five minutes but only scans sources whose own interval has elapsed, so a
        // source configured for daily indexing is still checked promptly after a restart
        // without needing a job per source.
        RecurringJob.AddOrUpdate<IIndexService>(
            "index-due-sources",
            Constants.HangfireQueues.Index,
            service => service.ScanAllDueAsync(CancellationToken.None),
            "*/5 * * * *",
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        RecurringJob.AddOrUpdate<IThumbnailService>(
            "prune-thumbnail-cache",
            Constants.HangfireQueues.Thumbnails,
            service => service.PruneAsync(CancellationToken.None),
            Cron.Daily(3),
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        RecurringJob.AddOrUpdate<IActivityService>(
            "purge-old-activity",
            Constants.HangfireQueues.Default,
            service => service.PurgeOlderThanAsync(ActivityRetentionDays, CancellationToken.None),
            Cron.Daily(4),
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
    }

    /// <summary>
    /// How long the audit trail is kept. A year is long enough to answer "who deleted that?"
    /// while keeping the table from growing without bound on a busy server.
    /// </summary>
    private const int ActivityRetentionDays = 365;
}