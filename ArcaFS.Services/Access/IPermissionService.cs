namespace ArcaFS.Services.Access;

/// <summary>
/// The single gate every filesystem operation passes through. Services never read
/// <c>AccessRule</c> directly; they call <see cref="RequireAsync"/> or
/// <see cref="GetPermissionsAsync"/> and act on the answer.
/// </summary>
public interface IPermissionService
{
    /// <summary>Builds the calling user's principal, including their group and role memberships.</summary>
    Task<AccessPrincipal> GetCurrentPrincipalAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Effective permissions for the current caller at <paramref name="virtualPath"/>. The path
    /// is interpreted as scope-relative and rebased onto the caller's scope first.
    /// </summary>
    Task<FilePermission> GetPermissionsAsync(
        int sourceId,
        string virtualPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a path to something actionable, or fails with the reason. This is the method
    /// callers should use: it returns the physical path, the scoped virtual path and the
    /// effective mask together, having already checked <paramref name="required"/>.
    /// </summary>
    Task<Result<ResolvedPath>> RequireAsync(
        int sourceId,
        string virtualPath,
        FilePermission required,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Same as <see cref="RequireAsync"/> but for an explicit principal, used by the share
    /// pipeline where the caller is an anonymous visitor with a share-derived ceiling.
    /// </summary>
    Task<Result<ResolvedPath>> RequireAsync(
        int sourceId,
        string virtualPath,
        FilePermission required,
        AccessPrincipal principal,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk resolution for a directory listing. Far cheaper than one <see cref="RequireAsync"/>
    /// per child because the source's rules are loaded and pre-filtered once.
    /// <paramref name="directoryVirtualPath"/> is the absolute (source) path of the directory,
    /// including any user-scope prefix; the returned function expects absolute child paths too.
    /// </summary>
    Task<Func<string, FilePermission>> GetChildPermissionEvaluatorAsync(
        int sourceId,
        string directoryVirtualPath,
        AccessPrincipal principal,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Full explanation of a permission decision, for the admin "who can do what here?" tool.
    /// Administrator-only.
    /// </summary>
    Task<Result<EffectivePermissionsDto>> ExplainAsync(
        EffectivePermissionsRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sources the caller can see at all, each with their effective permissions at their own
    /// root. Backs the sidebar and the source picker.
    /// </summary>
    Task<IReadOnlyList<SourceSummaryDto>> GetAccessibleSourcesAsync(CancellationToken cancellationToken = default);

    /// <summary>Drops cached rules for a source after its ACL or configuration changes.</summary>
    void InvalidateSource(int sourceId);
}

/// <summary>
/// A path that has been permission-checked and mapped onto the filesystem. Holding these three
/// values together means downstream code can't accidentally use an unchecked path.
/// </summary>
public sealed record ResolvedPath
{
    public required Source Source { get; init; }

    /// <summary>Absolute path on disk, verified to be inside <see cref="Source"/>'s root.</summary>
    public required string PhysicalPath { get; init; }

    /// <summary>Full path within the source, including the caller's scope prefix.</summary>
    public required string AbsoluteVirtualPath { get; init; }

    /// <summary>Path as the caller sees it, with their scope prefix removed.</summary>
    public required string ScopedVirtualPath { get; init; }

    /// <summary>The caller's scope within the source, or <c>/</c> when unscoped.</summary>
    public required string Scope { get; init; }

    public FilePermission Permissions { get; init; }

    public bool Has(FilePermission required) => Permissions.Has(required);
}
