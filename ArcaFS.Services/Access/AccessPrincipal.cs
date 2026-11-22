namespace ArcaFS.Services.Access;

/// <summary>
/// Everything the permission resolver needs to know about *who* is asking. Built once per
/// request from the current identity (or synthesised for a share visitor) and then reused for
/// every path check in that request, so group and role lookups happen once rather than per file.
/// </summary>
public sealed record AccessPrincipal
{
    /// <summary>
    /// User id, or <see cref="Constants.AnonymousUserId"/> for <see cref="Anonymous"/>. A signed-in
    /// caller with no id falls back to their user name.
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>Login name. <see langword="null"/> for <see cref="Anonymous"/>.</summary>
    public string? UserName { get; init; }

    /// <summary>
    /// Optional friendly name. When set, <c>${username}</c> in scopes and rules uses this so
    /// home folders match the name people actually create on disk.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Value substituted for <c>${username}</c>: display name when present, otherwise login name.
    /// </summary>
    public string? FolderName =>
        !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName : UserName;

    /// <summary>Whether this principal came from a signed-in identity.</summary>
    public bool IsAuthenticated { get; init; }

    /// <summary>Whether this principal is an administrator. Anonymous is not.</summary>
    public bool IsAdministrator { get; init; }

    /// <summary>Role names, compared ordinal and case-insensitive. Empty for anonymous.</summary>
    public IReadOnlySet<string> RoleNames { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Access-group ids this user belongs to. Empty when there is no user id to look up.</summary>
    public IReadOnlySet<int> GroupIds { get; init; } = new HashSet<int>();

    /// <summary>
    /// Hard ceiling applied after rule evaluation. Used for the global per-user share switch and
    /// for share links, where the visitor can never exceed what the share itself permits.
    /// </summary>
    public FilePermission Ceiling { get; init; } = FilePermission.Full;

    /// <summary>An unauthenticated share visitor.</summary>
    public static AccessPrincipal Anonymous { get; } = new()
    {
        UserId = Constants.AnonymousUserId,
        UserName = null,
        IsAuthenticated = false,
        IsAdministrator = false,
    };
}