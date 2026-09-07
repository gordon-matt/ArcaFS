namespace ArcaFS.Models;

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
