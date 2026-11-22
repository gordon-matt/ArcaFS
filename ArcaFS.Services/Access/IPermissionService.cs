namespace ArcaFS.Services.Access;

/// <summary>
/// The single gate every filesystem operation passes through. Services never read
/// <c>AccessRule</c> directly; they call <see cref="RequireAsync"/> or
/// <see cref="GetPermissionsAsync"/> and act on the answer.
/// </summary>
public interface IPermissionService
{
    /// <summary>Builds the calling user's principal, including their group and role memberships.</summary>
    /// <param name="cancellationToken">Cancels the membership lookup.</param>
    /// <returns>
    /// The principal for this request. An unauthenticated caller is <see cref="AccessPrincipal.Anonymous"/>.
    /// </returns>
    Task<AccessPrincipal> GetCurrentPrincipalAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Effective permissions for the current caller at <paramref name="virtualPath"/>. The path
    /// is interpreted as scope-relative and rebased onto the caller's scope first.
    /// </summary>
    /// <param name="sourceId">Source the path belongs to.</param>
    /// <param name="virtualPath">Path as the caller sees it.</param>
    /// <param name="cancellationToken">Cancels the rule lookup.</param>
    /// <returns>The effective permission flags. A missing source returns <see cref="FilePermission.None"/>.</returns>
    Task<FilePermission> GetPermissionsAsync(
        int sourceId,
        string virtualPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a path to something actionable, or fails with the reason. This is the method
    /// callers should use: it returns the physical path, the scoped virtual path and the
    /// effective mask together, having already checked <paramref name="required"/>.
    /// </summary>
    /// <param name="sourceId">Source the path belongs to.</param>
    /// <param name="virtualPath">Path as the caller sees it.</param>
    /// <param name="required">Flags that must all be present.</param>
    /// <param name="cancellationToken">Cancels the rule lookup.</param>
    /// <returns>The resolved path, or a failure when the source or path is unavailable or a required flag is missing.</returns>
    Task<Result<ResolvedPath>> RequireAsync(
        int sourceId,
        string virtualPath,
        FilePermission required,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Same as <see cref="RequireAsync"/> but for an explicit principal, used by the share
    /// pipeline where the caller is an anonymous visitor with a share-derived ceiling.
    /// </summary>
    /// <param name="sourceId">Source the path belongs to.</param>
    /// <param name="virtualPath">Path as the visitor sees it, inside the share.</param>
    /// <param name="required">Flags that must all be present.</param>
    /// <param name="principal">Visitor or user to evaluate. Not taken from the current request.</param>
    /// <param name="cancellationToken">Cancels the rule lookup.</param>
    /// <returns>The resolved path, or a failure when the path is outside the principal's scope or a required flag is missing.</returns>
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
    /// <param name="sourceId">Source the directory belongs to.</param>
    /// <param name="directoryVirtualPath">Absolute path of the directory within the source.</param>
    /// <param name="principal">Caller the child permissions are evaluated for.</param>
    /// <param name="cancellationToken">Cancels the rule load.</param>
    /// <returns>
    /// A function from an absolute child path to that child's effective permissions. The function
    /// itself does not touch the database.
    /// </returns>
    Task<Func<string, FilePermission>> GetChildPermissionEvaluatorAsync(
        int sourceId,
        string directoryVirtualPath,
        AccessPrincipal principal,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Full explanation of a permission decision, for the admin "who can do what here?" tool.
    /// Administrator-only.
    /// </summary>
    /// <param name="request">Source, path, and the user to explain.</param>
    /// <param name="cancellationToken">Cancels the rule lookup.</param>
    /// <returns>The effective mask and the rules that produced it, or forbidden when the caller is not an administrator.</returns>
    Task<Result<EffectivePermissionsDto>> ExplainAsync(
        EffectivePermissionsRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sources the caller can see at all, each with their effective permissions at their own
    /// root. Backs the sidebar and the source picker.
    /// </summary>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The visible sources. A caller who can see nothing gets an empty list.</returns>
    Task<IReadOnlyList<SourceSummaryDto>> GetAccessibleSourcesAsync(CancellationToken cancellationToken = default);

    /// <summary>Drops cached rules for a source after its ACL or configuration changes.</summary>
    /// <param name="sourceId">Source whose rule cache is removed. The cached source list is dropped as well.</param>
    void InvalidateSource(int sourceId);
}

/// <summary>
/// A path that has been permission-checked and mapped onto the filesystem. Holding these three
/// values together means downstream code can't accidentally use an unchecked path.
/// </summary>
public sealed record ResolvedPath
{
    /// <summary>The source the path was resolved against.</summary>
    public required Source Source { get; init; }

    /// <summary>Absolute path on disk, verified to be inside <see cref="Source"/>'s root.</summary>
    public required string PhysicalPath { get; init; }

    /// <summary>Full path within the source, including the caller's scope prefix.</summary>
    public required string AbsoluteVirtualPath { get; init; }

    /// <summary>Path as the caller sees it, with their scope prefix removed.</summary>
    public required string ScopedVirtualPath { get; init; }

    /// <summary>The caller's scope within the source, or <c>/</c> when unscoped.</summary>
    public required string Scope { get; init; }

    /// <summary>Effective permissions at <see cref="ScopedVirtualPath"/>.</summary>
    public FilePermission Permissions { get; init; }

    /// <summary>Whether <see cref="Permissions"/> includes every flag in <paramref name="required"/>.</summary>
    /// <param name="required">Flags that must all be present.</param>
    /// <returns><see langword="true"/> when every required flag is set.</returns>
    public bool Has(FilePermission required) => Permissions.Has(required);
}
