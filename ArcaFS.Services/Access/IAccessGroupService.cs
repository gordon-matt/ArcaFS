namespace ArcaFS.Services.Access;

/// <summary>Administrator-only management of the groups that access rules can target.</summary>
public interface IAccessGroupService
{
    Task<Result<IReadOnlyList<AccessGroupDto>>> ListAsync(CancellationToken cancellationToken = default);

    Task<Result<AccessGroupDto>> SaveAsync(
        SaveAccessGroupRequest request,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(int groupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces a user's externally-managed group memberships from an identity provider's
    /// groups claim, creating any group that doesn't exist yet. Called on Keycloak sign-in.
    /// </summary>
    Task<Result> SyncExternalGroupsAsync(
        string userId,
        IEnumerable<string> groupNames,
        CancellationToken cancellationToken = default);
}
