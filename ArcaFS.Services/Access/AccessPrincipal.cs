namespace ArcaFS.Services.Access;

/// <summary>
/// Everything the permission resolver needs to know about *who* is asking. Built once per
/// request from the current identity (or synthesised for a share visitor) and then reused for
/// every path check in that request, so group and role lookups happen once rather than per file.
/// </summary>
public sealed record AccessPrincipal
{
    public string? UserId { get; init; }

    public string? UserName { get; init; }

    public bool IsAuthenticated { get; init; }

    public bool IsAdministrator { get; init; }

    public IReadOnlySet<string> RoleNames { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
