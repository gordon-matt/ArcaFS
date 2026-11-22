namespace ArcaFS.Models;

/// <summary>One public link, as the owner or an administrator sees it.</summary>
/// <param name="Id">Share id.</param>
/// <param name="Token">Public token used in <c>/s/{token}</c>. This is not the password.</param>
/// <param name="SourceId">Source the shared path belongs to.</param>
/// <param name="SourceName">That source's display name.</param>
/// <param name="Path">Absolute path within the source. A scoped creator's prefix is included.</param>
/// <param name="OwnerUserId">User who created the link.</param>
/// <param name="OwnerDisplayName">That user's label, when it could be resolved.</param>
/// <param name="Title">Optional title shown to visitors.</param>
/// <param name="Description">Optional description shown to visitors.</param>
/// <param name="Mode">What a visitor is allowed to do.</param>
/// <param name="HasPassword">Whether a password is set. The password itself is not returned.</param>
/// <param name="ExpiresAt">UTC expiry, or <see langword="null"/> when the link does not expire.</param>
/// <param name="MaxDownloads">Download cap, or <see langword="null"/> when there is no cap.</param>
/// <param name="DownloadCount">Downloads recorded so far.</param>
/// <param name="DisableThumbnails">When <see langword="true"/>, the public page does not offer thumbnails.</param>
/// <param name="DisableFileViewer">When <see langword="true"/>, the public page does not open the in-browser viewer.</param>
/// <param name="Theme">Optional theme for the public page.</param>
/// <param name="CreatedAt">When the link was created, in UTC.</param>
/// <param name="LastAccessedAt">When it was last used, in UTC, or <see langword="null"/>.</param>
/// <param name="IsDisabled">When <see langword="true"/>, the link is turned off without being deleted.</param>
public record ShareDto(
    int Id,
    string Token,
    int SourceId,
    string SourceName,
    string Path,
    string OwnerUserId,
    string? OwnerDisplayName,
    string? Title,
    string? Description,
    ShareMode Mode,
    bool HasPassword,
    DateTime? ExpiresAt,
    int? MaxDownloads,
    int DownloadCount,
    bool DisableThumbnails,
    bool DisableFileViewer,
    string? Theme,
    DateTime CreatedAt,
    DateTime? LastAccessedAt,
    bool IsDisabled)
{
    public bool IsExpired => ExpiresAt is DateTime expiry && expiry <= DateTime.UtcNow;

    public bool IsExhausted => MaxDownloads is int max && DownloadCount >= max;

    public bool IsActive => !IsDisabled && !IsExpired && !IsExhausted;
}

public record CreateShareRequest
{
    public int SourceId { get; init; }

    public string Path { get; init; } = string.Empty;

    public string? Title { get; init; }

    public string? Description { get; init; }

    public ShareMode Mode { get; init; } = ShareMode.ReadOnly;

    /// <summary>Null or empty leaves the share unprotected.</summary>
    public string? Password { get; init; }

    /// <summary>Relative lifetime. Null never expires.</summary>
    public TimeSpan? ExpiresIn { get; init; }

    public int? MaxDownloads { get; init; }

    public bool DisableThumbnails { get; init; }

    public bool DisableFileViewer { get; init; }

    public string? Theme { get; init; }
}

public record UpdateShareRequest : CreateShareRequest
{
    public int Id { get; init; }

    /// <summary>Clears the existing password. Distinguishes "remove" from "leave unchanged".</summary>
    public bool RemovePassword { get; init; }

    public bool IsDisabled { get; init; }
}

/// <summary>
/// What a share visitor is told before authenticating. Deliberately omits the real source name
/// and path so an unauthenticated probe can't map the server's layout.
/// </summary>
public record PublicShareInfoDto(
    string Token,
    string? Title,
    string? Description,
    bool RequiresPassword,
    bool IsSingleFile,
    string? FileName,
    long? FileSizeBytes,
    FileKind Kind,
    ShareMode Mode,
    bool DisableThumbnails,
    bool DisableFileViewer,
    string? Theme,
    DateTime? ExpiresAt);