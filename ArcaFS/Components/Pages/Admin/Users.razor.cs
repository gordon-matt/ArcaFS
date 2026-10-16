using ArcaFS.Components.Shared;

namespace ArcaFS.Components.Pages.Admin;

public partial class Users
{
    private string UsersDescription => AuthProviderService.Provider switch
    {
        AuthProvider.Identity => "Local accounts (ASP.NET Identity). File access is granted on the Access rules page, not here.",
        AuthProvider.Keycloak => "Accounts live in Keycloak. This list is read-only. File access is still configured under Access rules.",
        _ => "Authentication is off - only the synthetic local user exists.",
    };

    private IAdminUserManagementService? adminUsers;
    private IReadOnlyList<RoleOption> assignableRoles = [];
    private string? bannerError;
    private string? currentUserId;
    private string? editingUserId;
    private UserForm form = new();
    private IReadOnlyList<AccessGroupDto> groups = [];
    private IReadOnlyList<AdminUserListItem>? identityRows;
    private bool identityLoading = true;
    private bool isCreate;
    private string? modalError;
    private IReadOnlyList<UserInfo>? readOnlyUsers;
    private bool saving;
    private bool showEditor;

    protected override async Task OnInitializedAsync()
    {
        adminUsers = Services.GetService<IAdminUserManagementService>();
        currentUserId = UserContext.GetCurrentUserId();

        var groupResult = await AccessGroupService.ListAsync();
        groups = groupResult.IsSuccess ? groupResult.Value : [];

        if (AuthProviderService.Provider == AuthProvider.Identity && adminUsers is not null)
        {
            await LoadIdentityAsync();
        }
        else
        {
            readOnlyUsers = await UserInfoService.GetAllUsersAsync();
        }
    }

    private async Task LoadIdentityAsync()
    {
        if (adminUsers is null)
        {
            return;
        }

        identityLoading = true;
        bannerError = null;

        var listResult = await adminUsers.ListUsersAsync();
        var rolesResult = await adminUsers.GetAssignableRolesAsync();

        identityRows = listResult.IsSuccess ? listResult.Value : [];
        if (!listResult.IsSuccess)
        {
            bannerError = listResult.GetDisplayMessage();
        }

        assignableRoles = rolesResult.IsSuccess ? rolesResult.Value : [];
        identityLoading = false;
    }

    private void OpenCreate()
    {
        isCreate = true;
        editingUserId = null;
        form = new UserForm { Roles = [WellKnownRoles.User] };
        modalError = null;
        showEditor = true;
    }

    private void OpenEdit(AdminUserListItem user)
    {
        isCreate = false;
        editingUserId = user.Id;
        form = new UserForm
        {
            UserName = user.UserName ?? string.Empty,
            Email = user.Email ?? string.Empty,
            DisplayName = user.DisplayName,
            CanCreateShares = user.CanCreateShares,
            CanUseApi = user.CanUseApi,
            Roles = user.Roles.ToList(),
            GroupIds = groups.Where(g => user.GroupNames.Contains(g.Name)).Select(g => g.Id).ToList(),
        };
        modalError = null;
        showEditor = true;
    }

    private void CloseEditor()
    {
        showEditor = false;
        modalError = null;
    }

    private void ToggleRole(string role, bool on)
    {
        if (on)
        {
            if (!form.Roles.Contains(role))
            {
                form.Roles.Add(role);
            }
        }
        else
        {
            form.Roles.Remove(role);
        }
    }

    private void ToggleGroup(int groupId, bool on)
    {
        if (on)
        {
            if (!form.GroupIds.Contains(groupId))
            {
                form.GroupIds.Add(groupId);
            }
        }
        else
        {
            form.GroupIds.Remove(groupId);
        }
    }

    private async Task SaveAsync()
    {
        if (adminUsers is null)
        {
            return;
        }

        saving = true;
        modalError = null;

        try
        {
            Result result;
            if (isCreate)
            {
                result = await adminUsers.CreateUserAsync(new AdminCreateUserRequest
                {
                    UserName = form.UserName,
                    Email = form.Email,
                    Password = form.Password,
                    DisplayName = form.DisplayName,
                    Roles = form.Roles,
                    CanCreateShares = form.CanCreateShares,
                    CanUseApi = form.CanUseApi,
                    AccessGroupIds = form.GroupIds,
                });
            }
            else if (!string.IsNullOrEmpty(editingUserId))
            {
                result = await adminUsers.UpdateUserAsync(editingUserId, new AdminUpdateUserRequest
                {
                    Email = form.Email,
                    DisplayName = form.DisplayName,
                    NewPassword = string.IsNullOrEmpty(form.Password) ? null : form.Password,
                    Roles = form.Roles,
                    CanCreateShares = form.CanCreateShares,
                    CanUseApi = form.CanUseApi,
                    AccessGroupIds = form.GroupIds,
                });
            }
            else
            {
                modalError = "Nothing to save.";
                return;
            }

            if (result.IsSuccess)
            {
                CloseEditor();
                Toasts.Success(isCreate ? "User created." : "User updated.");
                await LoadIdentityAsync();
            }
            else
            {
                modalError = result.GetDisplayMessage();
            }
        }
        finally
        {
            saving = false;
        }
    }

    private async Task ToggleLockoutAsync(AdminUserListItem user)
    {
        if (adminUsers is null)
        {
            return;
        }

        string action = user.IsLockedOut ? "unlock" : "lock";
        if (!await Dialogs.ConfirmAsync(
                $"{char.ToUpperInvariant(action[0])}{action[1..]} user",
                $"Are you sure you want to {action} “{user.UserName}”?",
                char.ToUpperInvariant(action[0]) + action[1..]))
        {
            return;
        }

        var result = await adminUsers.ToggleLockoutAsync(user.Id);
        if (result.IsSuccess)
        {
            await LoadIdentityAsync();
        }
        else
        {
            bannerError = result.GetDisplayMessage();
        }
    }

    private async Task DeleteAsync(AdminUserListItem user)
    {
        if (adminUsers is null)
        {
            return;
        }

        if (!await Dialogs.ConfirmAsync(
                "Delete user",
                $"Delete “{user.UserName}”? This cannot be undone.",
                "Delete",
                isDestructive: true,
                detail: "Access rules that name them stay in place and should be reviewed."))
        {
            return;
        }

        var result = await adminUsers.DeleteUserAsync(user.Id);
        if (result.IsSuccess)
        {
            Toasts.Success("User deleted.");
            await LoadIdentityAsync();
        }
        else
        {
            bannerError = result.GetDisplayMessage();
        }
    }

    private sealed class UserForm
    {
        public string UserName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string Password { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = [];
        public List<int> GroupIds { get; set; } = [];
        public bool CanCreateShares { get; set; } = true;
        public bool CanUseApi { get; set; }
    }
}
