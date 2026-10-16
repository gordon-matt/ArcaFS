namespace ArcaFS.Services.Auth;

/// <summary>
/// Reports the one synthetic user that exists in <c>None</c> mode. The id, name and roles come
/// from the library's <see cref="IUserContextService"/>, which owns that user.
/// </summary>
public sealed class NoneUserInfoService(IUserContextService userContext) : IUserInfoService
{
    private UserInfo DefaultUser
    {
        get
        {
            string id = userContext.GetCurrentUserId() ?? string.Empty;
            string name = userContext.GetCurrentUserName() ?? id;

            return new UserInfo(id, name, Email: null, DisplayName: name, Roles: [.. userContext.GetRoleNames()]);
        }
    }

    public Task<IReadOnlyDictionary<string, UserInfo>> GetUserInfoAsync(
        IEnumerable<string> userIds,
        CancellationToken cancellationToken = default)
    {
        var user = DefaultUser;

        IReadOnlyDictionary<string, UserInfo> result = userIds.Contains(user.Id)
            ? new Dictionary<string, UserInfo> { [user.Id] = user }
            : new Dictionary<string, UserInfo>();

        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<UserInfo>> GetAllUsersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<UserInfo>>([DefaultUser]);

    public Task<UserInfo?> GetUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = DefaultUser;
        return Task.FromResult(userId == user.Id ? user : null);
    }
}
