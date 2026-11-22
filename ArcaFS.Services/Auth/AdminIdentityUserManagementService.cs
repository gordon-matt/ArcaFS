using Microsoft.AspNetCore.Identity;

namespace ArcaFS.Services.Auth;

/// <summary>
/// User administration against the local Identity tables. Registered only when Identity is the
/// active provider; under Keycloak the IdP owns these operations.
/// </summary>
public sealed class AdminIdentityUserManagementService(
    ILogger<AdminIdentityUserManagementService> logger,
    IUserContextService userContext,
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    IRepository<AccessGroup> groupRepository,
    IRepository<AccessGroupMember> memberRepository,
    IPermissionService permissionService) : IAdminUserManagementService
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<AdminUserListItem>>> ListUsersAsync(
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result<IReadOnlyList<AdminUserListItem>>.Forbidden();
        }

        var users = await userManager.Users
            .AsNoTracking()
            .OrderBy(u => u.UserName)
            .ToListAsync(cancellationToken);

        var groupNamesByUser = await LoadGroupNamesAsync(cancellationToken);
        var items = new List<AdminUserListItem>(users.Count);

        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);

            items.Add(new AdminUserListItem(
                user.Id,
                user.UserName,
                user.Email,
                user.DisplayName,
                roles.ToList(),
                await userManager.IsLockedOutAsync(user),
                user.EmailConfirmed,
                user.CanCreateShares,
                user.CanUseApi,
                user.CreatedAt,
                user.LastSeenAt,
                groupNamesByUser.GetValueOrDefault(user.Id) ?? []));
        }

        return Result<IReadOnlyList<AdminUserListItem>>.Success(items);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<RoleOption>>> GetAssignableRolesAsync(
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result<IReadOnlyList<RoleOption>>.Forbidden();
        }

        var roles = await roleManager.Roles
            .AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new RoleOption(r.Name!, r.Description))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<RoleOption>>.Success(roles);
    }

    /// <inheritdoc />
    public async Task<Result> CreateUserAsync(
        AdminCreateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        if (string.IsNullOrWhiteSpace(request.UserName))
        {
            return Result.Invalid(new ValidationError(nameof(request.UserName), "Enter a username."));
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return Result.Invalid(new ValidationError(nameof(request.Password), "Enter a password."));
        }

        var user = new ApplicationUser
        {
            UserName = request.UserName.Trim(),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            EmailConfirmed = true,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
                ? request.UserName.Trim()
                : request.DisplayName.Trim(),
            CanCreateShares = request.CanCreateShares,
            CanUseApi = request.CanUseApi,
        };

        var created = await userManager.CreateAsync(user, request.Password);

        if (!created.Succeeded)
        {
            return ToValidationResult(created, nameof(request.Password));
        }

        var roles = NormalizeRoles(request.Roles);
        if (roles.Count > 0)
        {
            await userManager.AddToRolesAsync(user, roles);
        }

        await ReplaceGroupMembershipsAsync(user.Id, request.AccessGroupIds, cancellationToken);

        logger.LogInformation(
            "User '{UserName}' created by {Administrator}",
            user.UserName,
            userContext.GetCurrentUserName());

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> UpdateUserAsync(
        string userId,
        AdminUpdateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return Result.NotFound($"User {userId} was not found.");
        }

        var roles = NormalizeRoles(request.Roles);

        var guard = await GuardLastAdministratorAsync(user, roles);
        if (!guard.IsSuccess)
        {
            return guard;
        }

        user.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        user.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
            ? user.UserName
            : request.DisplayName.Trim();
        user.CanCreateShares = request.CanCreateShares;
        user.CanUseApi = request.CanUseApi;

        var updated = await userManager.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            return ToValidationResult(updated, nameof(request.Email));
        }

        if (!string.IsNullOrEmpty(request.NewPassword))
        {
            // Reset rather than change: an administrator setting someone else's password does
            // not know the current one.
            string token = await userManager.GeneratePasswordResetTokenAsync(user);
            var reset = await userManager.ResetPasswordAsync(user, token, request.NewPassword);

            if (!reset.Succeeded)
            {
                return ToValidationResult(reset, nameof(request.NewPassword));
            }
        }

        var currentRoles = await userManager.GetRolesAsync(user);

        var toRemove = currentRoles.Except(roles, StringComparer.OrdinalIgnoreCase).ToList();
        if (toRemove.Count > 0)
        {
            await userManager.RemoveFromRolesAsync(user, toRemove);
        }

        var toAdd = roles.Except(currentRoles, StringComparer.OrdinalIgnoreCase).ToList();
        if (toAdd.Count > 0)
        {
            await userManager.AddToRolesAsync(user, toAdd);
        }

        await ReplaceGroupMembershipsAsync(user.Id, request.AccessGroupIds, cancellationToken);

        // Roles and groups both feed the permission resolver's principal, and both are cached.
        permissionService.InvalidateSource(0);

        logger.LogInformation(
            "User '{UserName}' updated by {Administrator}",
            user.UserName,
            userContext.GetCurrentUserName());

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> ToggleLockoutAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return Result.NotFound($"User {userId} was not found.");
        }

        if (string.Equals(user.Id, userContext.GetCurrentUserId(), StringComparison.Ordinal))
        {
            return Result.Invalid(new ValidationError(
                nameof(userId), "You cannot lock your own account."));
        }

        bool isLockedOut = await userManager.IsLockedOutAsync(user);

        // A far-future date rather than a duration, so the lock stays until it is lifted.
        var until = isLockedOut ? null : (DateTimeOffset?)DateTimeOffset.MaxValue;

        await userManager.SetLockoutEndDateAsync(user, until);

        logger.LogInformation(
            "User '{UserName}' was {Action} by {Administrator}",
            user.UserName,
            isLockedOut ? "unlocked" : "locked out",
            userContext.GetCurrentUserName());

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> DeleteUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return Result.NotFound($"User {userId} was not found.");
        }

        if (string.Equals(user.Id, userContext.GetCurrentUserId(), StringComparison.Ordinal))
        {
            return Result.Invalid(new ValidationError(
                nameof(userId), "You cannot delete your own account."));
        }

        var guard = await GuardLastAdministratorAsync(user, []);
        if (!guard.IsSuccess)
        {
            return guard;
        }

        // Group memberships have no cascade back to Identity's user table, so they would
        // otherwise be left pointing at a user that no longer exists.
        await ReplaceGroupMembershipsAsync(user.Id, [], cancellationToken);

        var deleted = await userManager.DeleteAsync(user);

        if (!deleted.Succeeded)
        {
            return Result.Error(new ErrorList(deleted.Errors.Select(e => e.Description).ToArray()));
        }

        logger.LogInformation(
            "User '{UserName}' deleted by {Administrator}. Access rules naming them remain and " +
            "should be reviewed.",
            user.UserName,
            userContext.GetCurrentUserName());

        return Result.Success();
    }

    /// <summary>
    /// Refuses to remove the administrator role from, or delete, the only administrator left.
    /// Locking everyone out of their own server is unrecoverable without database access.
    /// </summary>
    private async Task<Result> GuardLastAdministratorAsync(
        ApplicationUser user,
        IReadOnlyCollection<string> newRoles)
    {
        bool wasAdministrator = await userManager.IsInRoleAsync(user, WellKnownRoles.Administrator);

        if (!wasAdministrator)
        {
            return Result.Success();
        }

        bool staysAdministrator = newRoles.Contains(
            WellKnownRoles.Administrator,
            StringComparer.OrdinalIgnoreCase);

        if (staysAdministrator)
        {
            return Result.Success();
        }

        var administrators = await userManager.GetUsersInRoleAsync(WellKnownRoles.Administrator);

        return administrators.Count <= 1
            ? Result.Invalid(new ValidationError(
                nameof(user.Id),
                "This is the only administrator. Promote another user first."))
            : Result.Success();
    }

    private async Task<Dictionary<string, List<string>>> LoadGroupNamesAsync(
        CancellationToken cancellationToken)
    {
        var groups = await groupRepository.FindAsync(new SearchOptions<AccessGroup>
        {
            Include = q => q.Include(g => g.Members),
            CancellationToken = cancellationToken,
        });

        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var group in groups)
        {
            foreach (var member in group.Members)
            {
                if (!map.TryGetValue(member.UserId, out var names))
                {
                    names = [];
                    map[member.UserId] = names;
                }

                names.Add(group.Name);
            }
        }

        return map;
    }

    private async Task ReplaceGroupMembershipsAsync(
        string userId,
        IReadOnlyList<int> groupIds,
        CancellationToken cancellationToken)
    {
        var existing = (await memberRepository.FindAsync(new SearchOptions<AccessGroupMember>
        {
            Query = m => m.UserId == userId,
            CancellationToken = cancellationToken,
        })).ToList();

        var wanted = groupIds.Distinct().ToHashSet();

        var toRemove = existing.Where(m => !wanted.Contains(m.AccessGroupId)).ToList();
        if (toRemove.Count > 0)
        {
            await memberRepository.DeleteAsync(toRemove);
        }

        var currentIds = existing.Select(m => m.AccessGroupId).ToHashSet();
        var toAdd = wanted
            .Where(id => !currentIds.Contains(id))
            .Select(id => new AccessGroupMember { AccessGroupId = id, UserId = userId })
            .ToList();

        if (toAdd.Count > 0)
        {
            await memberRepository.InsertAsync(toAdd);
        }
    }

    private static List<string> NormalizeRoles(IReadOnlyList<string> roles) =>
        roles
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Identity's error descriptions are already written for end users, so they are surfaced as
    /// validation errors rather than replaced with something vaguer.
    /// </summary>
    private static Result ToValidationResult(IdentityResult result, string field) =>
        Result.Invalid(result.Errors
            .Select(e => new ValidationError(field, e.Description))
            .ToArray());
}