namespace ArcaFS.Services.Shares;

/// <summary>
/// An authorized share visit. Carries the principal the visitor browses as, plus the shared
/// root, so every path they request can be rebased onto that root and checked against a ceiling
/// they cannot exceed.
/// </summary>
public sealed record ShareSession
{
    public required string Token { get; init; }

    public required int SourceId { get; init; }

    /// <summary>The shared path, absolute within the source. Visitors never see this string.</summary>
    public required string RootPath { get; init; }

    /// <summary>True when the share points at a single file rather than a folder.</summary>
    public bool IsSingleFile { get; init; }

    public ShareMode Mode { get; init; }

    /// <summary>
    /// The synthetic visitor. Anonymous, with a <see cref="AccessPrincipal.Ceiling"/> derived
    /// from the share's mode and clamped to what its owner could do at the path.
    /// </summary>
    public required AccessPrincipal Principal { get; init; }

    public bool DisableThumbnails { get; init; }

    public bool DisableFileViewer { get; init; }

    public string? Theme { get; init; }

    /// <summary>
    /// Rebases a visitor-supplied relative path onto the shared root, returning null when it
    /// would escape. Escape is possible to *attempt* — a crafted request can contain anything —
    /// so this is the single point that decides what a share link can reach.
    /// </summary>
    public string? ResolveWithinShare(string? relativePath)
    {
        if (IsSingleFile)
        {
            // A single-file share exposes exactly one path; any sub-path request is bogus.
            string normalizedRelative = VirtualPath.Normalize(relativePath);

            return normalizedRelative == Constants.RootPath ? RootPath : null;
        }

        string combined = VirtualPath.Combine(
            RootPath,
            VirtualPath.GetSegments(VirtualPath.Normalize(relativePath)));

        // Normalize has already collapsed any "..", but re-checking containment means a future
        // change to path handling can't silently widen a share.
        return VirtualPath.IsSameOrUnder(combined, RootPath) ? combined : null;
    }

    /// <summary>The permission mask a given share mode implies.</summary>
    public static FilePermission CeilingFor(ShareMode mode) => mode switch
    {
        ShareMode.ReadOnly =>
            FilePermission.Browse | FilePermission.Read | FilePermission.Download,

        ShareMode.Upload =>
            FilePermission.Browse | FilePermission.Read | FilePermission.Download
            | FilePermission.Create,

        ShareMode.ReadWrite =>
            FilePermission.Browse | FilePermission.Read | FilePermission.Download
            | FilePermission.Create | FilePermission.Modify | FilePermission.Rename
            | FilePermission.Move | FilePermission.Delete,

        _ => FilePermission.None,
    };
}