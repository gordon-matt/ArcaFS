namespace ArcaFS.Services.Auth;

/// <summary>
/// Reads user identities from wherever they actually live — local Identity tables, Keycloak, or
/// nowhere at all in <c>None</c> mode. Access rules store bare user ids, so the rules editor and
/// activity log go through this to turn them into names.
/// </summary>
public interface IUserInfoService
{
    /// <summary>
    /// Looks up the given ids. Blank ids are ignored. An id that cannot be resolved is omitted
    /// rather than returned as a placeholder. Identity and Keycloak both return an empty
    /// dictionary when the lookup fails.
    /// </summary>
    /// <param name="userIds">Ids to resolve. Duplicates are read once.</param>
    /// <param name="cancellationToken">
    /// Cancels the lookup for Identity and Keycloak. <c>None</c> mode does not observe it.
    /// </param>
    /// <returns>The users that were found, keyed by id. Comparison is ordinal.</returns>
    Task<IReadOnlyDictionary<string, UserInfo>> GetUserInfoAsync(
        IEnumerable<string> userIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every user the active provider can list. Keycloak returns an empty list when the realm
    /// cannot be reached or the service account cannot list users. <c>None</c> mode returns the
    /// single synthetic user.
    /// </summary>
    /// <param name="cancellationToken">
    /// Cancels the lookup for Identity and Keycloak. <c>None</c> mode does not observe it.
    /// </param>
    /// <returns>The users, ordered by name where the provider supplies an order.</returns>
    Task<IReadOnlyList<UserInfo>> GetAllUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>One user, or <see langword="null"/> when that id is not known.</summary>
    /// <param name="userId">User id.</param>
    /// <param name="cancellationToken">
    /// Cancels the lookup for Identity and Keycloak. <c>None</c> mode does not observe it.
    /// </param>
    /// <returns>The user, or <see langword="null"/>.</returns>
    Task<UserInfo?> GetUserAsync(string userId, CancellationToken cancellationToken = default);
}
