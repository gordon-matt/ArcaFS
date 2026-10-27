namespace ArcaFS.Services.Auth;

/// <summary>
/// Reads user identities from wherever they actually live — local Identity tables, Keycloak, or
/// nowhere at all in <c>None</c> mode. Access rules store bare user ids, so the rules editor and
/// activity log go through this to turn them into names.
/// </summary>
public interface IUserInfoService
{
    Task<IReadOnlyDictionary<string, UserInfo>> GetUserInfoAsync(
        IEnumerable<string> userIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserInfo>> GetAllUsersAsync(CancellationToken cancellationToken = default);

    Task<UserInfo?> GetUserAsync(string userId, CancellationToken cancellationToken = default);
}