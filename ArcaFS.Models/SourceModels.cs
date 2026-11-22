namespace ArcaFS.Models;

/// <summary>Full administrative view of a source.</summary>
/// <param name="Id">Source id.</param>
/// <param name="Name">Display name.</param>
/// <param name="Path">Host directory this source is rooted at.</param>
/// <param name="Description">Optional description.</param>
/// <param name="IsDisabled">When <see langword="true"/>, the source is hidden from browsing.</param>
/// <param name="IsReadOnly">When <see langword="true"/>, mutating permissions are clamped off.</param>
/// <param name="DenyByDefault">When <see langword="true"/>, a path has no permissions until a rule grants them.</param>
/// <param name="DefaultPermissions">Baseline mask used when <paramref name="DenyByDefault"/> is <see langword="false"/>.</param>
/// <param name="AdministratorsBypassRules">When <see langword="true"/>, an administrator is not limited by the rules.</param>
/// <param name="DefaultUserScope">Starting folder template, or <see langword="null"/> when users see the whole source.</param>
/// <param name="CreateUserDirectory">When <see langword="true"/>, the starting folder is created on the user's first visit.</param>
/// <param name="IsVisibleInSidebar">When <see langword="false"/>, the source is left out of the sidebar.</param>
/// <param name="IgnoreHiddenFiles">When <see langword="true"/>, hidden files stay out of the listing until the caller opts in.</param>
/// <param name="FollowSymlinks">When <see langword="true"/>, directory walks follow reparse points.</param>
/// <param name="IsIndexed">When <see langword="true"/>, the source is included in the index.</param>
/// <param name="IndexIntervalMinutes">Minutes between scheduled scans. Zero means manual only.</param>
/// <param name="ExcludePatterns">Exclude globs, or <see langword="null"/> when nothing is excluded.</param>
/// <param name="LastIndexedAt">When the last scan finished, in UTC, or <see langword="null"/> if it has never finished.</param>
/// <param name="IndexedItemCount">Items stored by the last scan, or <see langword="null"/> if it has never finished.</param>
/// <param name="IndexedSizeBytes">Bytes stored by the last scan, or <see langword="null"/> if it has never finished.</param>
/// <param name="SortOrder">Sidebar and admin list order. Lower comes first.</param>
/// <param name="AccessRuleCount">How many access rules name this source.</param>
/// <param name="PathExists">Whether <paramref name="Path"/> is a directory at the time this value is read.</param>
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
/// <param name="Id">Source id.</param>
/// <param name="Name">Display name.</param>
/// <param name="Description">Optional description.</param>
/// <param name="RootPermissions">What the caller can do at their own root of this source.</param>
/// <param name="IsReadOnly">Whether the source clamps mutating permissions off.</param>
/// <param name="IndexedItemCount">Items from the last scan, or <see langword="null"/> if it has never finished.</param>
/// <param name="IndexedSizeBytes">Bytes from the last scan, or <see langword="null"/> if it has never finished.</param>
/// <param name="LastIndexedAt">When the last scan finished, in UTC.</param>
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