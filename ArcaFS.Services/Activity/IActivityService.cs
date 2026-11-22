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
    /// <param name="kind">What happened.</param>
    /// <param name="sourceId">Source the path belongs to, when there is one.</param>
    /// <param name="path">Path the event is about. Truncated to the stored maximum.</param>
    /// <param name="succeeded">Whether the operation itself succeeded. Defaults to <see langword="true"/>.</param>
    /// <param name="targetPath">Second path, such as a rename or move destination.</param>
    /// <param name="sizeBytes">Size involved, when the operation has one.</param>
    /// <param name="detail">Short extra text. Truncated before it is stored.</param>
    /// <param name="shareToken">Public share token, when a visitor caused the event.</param>
    /// <param name="overrideUserId">
    /// User id to store instead of the signed-in user. Used when a share visit has no account.
    /// </param>
    /// <param name="cancellationToken">Not observed. A failed insert is logged and swallowed.</param>
    /// <returns>A completed task. This does not report whether the row was stored.</returns>
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

    /// <summary>
    /// Pages the audit log, newest first. An administrator may filter by user. Anyone else sees
    /// only their own events, whatever user id they send. Page size is clamped to 1–500.
    /// </summary>
    /// <param name="request">Kind, user, source, time range, path text, success flag, and page.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The page. The total count is the number of matching events, not the page length.</returns>
    Task<PagedList<ActivityEventDto>> QueryAsync(
        ActivityQueryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes events older than <paramref name="retentionDays"/>. Called by a nightly job.</summary>
    /// <param name="retentionDays">
    /// How many days to keep. Zero or less deletes nothing.
    /// </param>
    /// <param name="cancellationToken">Cancels the lookup. The delete is not cancelled once it has started.</param>
    /// <returns>How many events were deleted.</returns>
    Task<int> PurgeOlderThanAsync(int retentionDays, CancellationToken cancellationToken = default);
}
