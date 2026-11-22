namespace ArcaFS.Services.Access;

/// <summary>Administrator-only management of the groups that access rules can target.</summary>
public interface IAccessGroupService
{
    /// <summary>Lists every group and its members, with names filled in from the identity provider.</summary>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The groups, ordered by name, or forbidden when the caller is not an administrator.</returns>
    Task<Result<IReadOnlyList<AccessGroupDto>>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a group when <see cref="SaveAccessGroupRequest.Id"/> is zero, otherwise replaces
    /// that group's name, description, and members. An externally managed group's membership
    /// cannot be edited here.
    /// </summary>
    /// <param name="request">Group id, name, description, and member user ids. The name is trimmed.</param>
    /// <param name="cancellationToken">Cancels the lookup and the write.</param>
    /// <returns>
    /// The saved group, or a failure when the name is blank, the name is taken, the group is
    /// missing, or the caller is not an administrator.
    /// </returns>
    Task<Result<AccessGroupDto>> SaveAsync(
        SaveAccessGroupRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a group that no access rule still names.</summary>
    /// <param name="groupId">Group id.</param>
    /// <param name="cancellationToken">Cancels the lookup. The delete is not cancelled once it has started.</param>
    /// <returns>
    /// Success, or a failure when the group is missing, a rule still targets it, or the caller
    /// is not an administrator.
    /// </returns>
    Task<Result> DeleteAsync(int groupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces a user's externally-managed group memberships from an identity provider's
    /// groups claim, creating any group that doesn't exist yet. Called on Keycloak sign-in.
    /// Locally assigned memberships are left alone. Blank names are ignored.
    /// </summary>
    /// <param name="userId">User whose external memberships are reconciled.</param>
    /// <param name="groupNames">
    /// Group names from the identity provider. A leading slash is stripped. Matching is ordinal
    /// and case-insensitive.
    /// </param>
    /// <param name="cancellationToken">Cancels the lookup and the membership writes.</param>
    /// <returns>Success once the external memberships match <paramref name="groupNames"/>.</returns>
    Task<Result> SyncExternalGroupsAsync(
        string userId,
        IEnumerable<string> groupNames,
        CancellationToken cancellationToken = default);
}
