namespace ArcaFS.Models;

/// <summary>Full administrative view of a source.</summary>
public record SourceDto(
    int Id,
    string Name,
    string Path,
    string? Description,
    bool IsDisabled,
    bool IsReadOnly,
    bool DenyByDefault,
    FilePermission DefaultPermissions,
    bool AdministratorsBypassRules,
    string? DefaultUserScope,
    bool CreateUserDirectory,
    bool IsVisibleInSidebar,
    bool IgnoreHiddenFiles,
    bool FollowSymlinks,
    bool IsIndexed,
    int IndexIntervalMinutes,
    string? ExcludePatterns,
    DateTime? LastIndexedAt,
    long? IndexedItemCount,
    long? IndexedSizeBytes,
    int SortOrder,
    int AccessRuleCount,
    bool PathExists);

/// <summary>
/// A source as the sidebar sees it: name, icon-worthy counts, and the calling user's effective
/// permissions at their own root so the UI can hide actions it would only reject later.
/// </summary>
public record SourceSummaryDto(
    int Id,
    string Name,
    string? Description,
    FilePermission RootPermissions,
    bool IsReadOnly,
    long? IndexedItemCount,
    long? IndexedSizeBytes,
    DateTime? LastIndexedAt);

public record CreateSourceRequest
{
    public string Name { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public string? Description { get; init; }

    public bool IsReadOnly { get; init; }

    public bool DenyByDefault { get; init; }

    public FilePermission DefaultPermissions { get; init; } = FilePermission.ReadOnly;

    public bool AdministratorsBypassRules { get; init; } = true;

    public string? DefaultUserScope { get; init; }

    public bool CreateUserDirectory { get; init; }

    public bool IsVisibleInSidebar { get; init; } = true;

    public bool IgnoreHiddenFiles { get; init; } = true;

    public bool FollowSymlinks { get; init; }

    public bool IsIndexed { get; init; } = true;

    public int IndexIntervalMinutes { get; init; } = 60;

    public string? ExcludePatterns { get; init; }
}

public record UpdateSourceRequest : CreateSourceRequest
{
    public int Id { get; init; }

    public bool IsDisabled { get; init; }

    public int SortOrder { get; init; }
}

/// <summary>Live index progress for one source, polled by the sources admin page.</summary>
public record SourceIndexStatusDto(
    int SourceId,
    bool IsScanning,
    DateTime? LastIndexedAt,
    long? IndexedItemCount,
    long? IndexedSizeBytes);

/// <summary>
/// The result of testing a host path before saving it as a source, so the admin sees "that
/// directory doesn't exist" while typing rather than after the fact.
/// </summary>
public record SourcePathProbeDto(
    string Path,
    bool Exists,
    bool IsDirectory,

    /// <summary>False when the server process cannot enumerate the directory.</summary>
    bool CanRead,

    /// <summary>False when the server process cannot create entries in it.</summary>
    bool CanWrite,

    int? ImmediateChildCount,
    string? Message);