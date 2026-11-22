namespace ArcaFS.Services.Auth;

/// <summary>
/// Administrative user CRUD. Only registered when ASP.NET Identity is the active provider —
/// under Keycloak users are managed in the IdP, and <c>None</c> mode has a single synthetic user.
/// </summary>
public interface IAdminUserManagementService
{
    /// <summary>Every local user, with roles, lockout, and access-group names.</summary>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The users, ordered by user name, or forbidden when the caller is not an administrator.</returns>
    Task<Result<IReadOnlyList<AdminUserListItem>>> ListUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>Roles an administrator can assign.</summary>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The roles, ordered by name, or forbidden when the caller is not an administrator.</returns>
    Task<Result<IReadOnlyList<RoleOption>>> GetAssignableRolesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a user. A blank display name stores the user name. Email confirmation is set,
    /// because an administrator creating the account has already checked it.
    /// </summary>
    /// <param name="request">User name, password, email, display name, roles, flags, and groups.</param>
    /// <param name="cancellationToken">Cancels the group-membership write. Creating the Identity user is not cancelled.</param>
    /// <returns>
    /// Success, or a failure when the caller is not an administrator or Identity rejects the
    /// user name or password.
    /// </returns>
    Task<Result> CreateUserAsync(AdminCreateUserRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces email, display name, share and API flags, roles, and group memberships. A
    /// non-empty new password resets the password. The last remaining administrator cannot lose
    /// that role. Cached permissions are dropped afterward.
    /// </summary>
    /// <param name="userId">User id.</param>
    /// <param name="request">Fields to store. An empty new password leaves the current password.</param>
    /// <param name="cancellationToken">Cancels the group-membership write. The Identity update is not cancelled.</param>
    /// <returns>Success, or a failure when the user is missing, the caller is not an administrator, or the change would remove the last administrator.</returns>
    Task<Result> UpdateUserAsync(
        string userId,
        AdminUpdateUserRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Locks a user until an administrator unlocks them, or unlocks a user who is locked.
    /// The caller cannot lock their own account.
    /// </summary>
    /// <param name="userId">User id.</param>
    /// <param name="cancellationToken">Not observed.</param>
    /// <returns>Success, or a failure when the user is missing, is the caller, or the caller is not an administrator.</returns>
    Task<Result> ToggleLockoutAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a user and their access-group memberships. Access rules that name the user are
    /// left in place. The caller cannot delete their own account or the last administrator.
    /// </summary>
    /// <param name="userId">User id.</param>
    /// <param name="cancellationToken">Cancels the membership cleanup. The Identity delete is not cancelled.</param>
    /// <returns>Success, or a failure when the user is missing or the delete is refused.</returns>
    Task<Result> DeleteUserAsync(string userId, CancellationToken cancellationToken = default);
}
