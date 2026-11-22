namespace ArcaFS.Services.Auth;

/// <summary>
/// Reads users straight from the Identity tables via <see cref="IDbContextFactory"/> rather than
/// <c>UserManager</c>, so it also works from Hangfire jobs that have no request scope.
/// </summary>
public sealed class AspNetIdentityUserInfoService(
    ILogger<AspNetIdentityUserInfoService> logger,
    IDbContextFactory dbContextFactory) : IUserInfoService
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, UserInfo>> GetUserInfoAsync(
        IEnumerable<string> userIds,
        CancellationToken cancellationToken = default)
    {
        var ids = userIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<string, UserInfo>();
        }

        var users = await LoadAsync(u => ids.Contains(u.Id), cancellationToken);
        return users.ToDictionary(u => u.Id, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserInfo>> GetAllUsersAsync(CancellationToken cancellationToken = default) =>
        await LoadAsync(predicate: null, cancellationToken);

    /// <inheritdoc />
    public async Task<UserInfo?> GetUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var users = await LoadAsync(u => u.Id == userId, cancellationToken);
        return users.FirstOrDefault();
    }

    private async Task<List<UserInfo>> LoadAsync(
        System.Linq.Expressions.Expression<Func<ApplicationUser, bool>>? predicate,
        CancellationToken cancellationToken)
    {
        try
        {
            using var context = (ApplicationDbContextBase)dbContextFactory.GetContext();

            var query = context.Users.AsNoTracking();
            if (predicate is not null)
            {
                query = query.Where(predicate);
            }

            var users = await query
                .OrderBy(u => u.UserName)
                .Select(u => new { u.Id, u.UserName, u.Email, u.DisplayName })
                .ToListAsync(cancellationToken);

            var userIds = users.Select(u => u.Id).ToList();

            // One join rather than a role lookup per user; the /users page lists everyone at once.
            var roleAssignments = await context.UserRoles
                .AsNoTracking()
                .Where(ur => userIds.Contains(ur.UserId))
                .Join(
                    context.Roles.AsNoTracking(),
                    ur => ur.RoleId,
                    r => r.Id,
                    (ur, r) => new { ur.UserId, RoleName = r.Name })
                .ToListAsync(cancellationToken);

            var rolesByUser = roleAssignments
                .Where(x => x.RoleName is not null)
                .GroupBy(x => x.UserId, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<string>)g.Select(x => x.RoleName!).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    StringComparer.Ordinal);

            return users
                .Select(u => new UserInfo(
                    u.Id,
                    u.UserName,
                    u.Email,
                    u.DisplayName,
                    rolesByUser.GetValueOrDefault(u.Id, [])))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load users from the Identity tables");
            return [];
        }
    }
}