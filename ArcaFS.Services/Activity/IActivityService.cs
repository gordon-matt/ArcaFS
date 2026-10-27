namespace ArcaFS.Services.Activity;

/// <summary>
/// Append-only audit trail. Every mutating filesystem operation and every share access records
/// an event here, which is what makes "who deleted that folder?" answerable after the fact.
/// </summary>
public interface IActivityService
{
    /// <summary>
    /// Records an event. Deliberately fire-and-forget in behaviour: auditing must never be the
    /// reason a user's file operation fails, so implementations swallow and log their own errors.
    /// </summary>
    Task RecordAsync(
        ActivityKind kind,
        int? sourceId,
        string? path,
        bool succeeded = true,
        string? targetPath = null,
        long? sizeBytes = null,
        string? detail = null,
        string? shareToken = null,
        string? overrideUserId = null,
        CancellationToken cancellationToken = default);

    Task<PagedList<ActivityEventDto>> QueryAsync(
        ActivityQueryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes events older than <paramref name="retentionDays"/>. Called by a nightly job.</summary>
    Task<int> PurgeOlderThanAsync(int retentionDays, CancellationToken cancellationToken = default);
}