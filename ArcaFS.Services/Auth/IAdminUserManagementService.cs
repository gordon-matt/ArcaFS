namespace ArcaFS.Services.Auth;

/// <summary>
/// Administrative user CRUD. Only registered when ASP.NET Identity is the active provider —
/// under Keycloak users are managed in the IdP, and <c>None</c> mode has a single synthetic user.
/// </summary>
public interface IAdminUserManagementService
{
    Task<Result<IReadOnlyList<AdminUserListItem>>> ListUsersAsync(CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<RoleOption>>> GetAssignableRolesAsync(CancellationToken cancellationToken = default);

    Task<Result> CreateUserAsync(AdminCreateUserRequest request, CancellationToken cancellationToken = default);

    Task<Result> UpdateUserAsync(
        string userId,
        AdminUpdateUserRequest request,
        CancellationToken cancellationToken = default);

    Task<Result> ToggleLockoutAsync(string userId, CancellationToken cancellationToken = default);

    Task<Result> DeleteUserAsync(string userId, CancellationToken cancellationToken = default);
}
