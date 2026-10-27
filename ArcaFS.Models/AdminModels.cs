namespace ArcaFS.Models;

/// <summary>Minimal identity projection, shared by every auth mode.</summary>
public record UserInfo(
    string Id,
    string? UserName,
    string? Email,
    string? DisplayName,
    IReadOnlyList<string> Roles)
{
    public string Label => !string.IsNullOrWhiteSpace(DisplayName)
        ? DisplayName
        : UserName ?? Email ?? Id;

    public bool IsAdministrator => Roles.Contains(WellKnownRoles.Administrator, StringComparer.OrdinalIgnoreCase);
}

public record AdminUserListItem(
    string Id,
    string? UserName,
    string? Email,
    string? DisplayName,
    IReadOnlyList<string> Roles,
    bool IsLockedOut,
    bool EmailConfirmed,
    bool CanCreateShares,
    bool CanUseApi,
    DateTime CreatedAt,
    DateTime? LastSeenAt,
    IReadOnlyList<string> GroupNames);

public record AdminCreateUserRequest
{
    public string UserName { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string? DisplayName { get; init; }

    public IReadOnlyList<string> Roles { get; init; } = [WellKnownRoles.User];

    public bool CanCreateShares { get; init; } = true;

    public bool CanUseApi { get; init; }

    public IReadOnlyList<int> AccessGroupIds { get; init; } = [];
}

public record AdminUpdateUserRequest
{
    public string? Email { get; init; }

    public string? DisplayName { get; init; }

    /// <summary>Null or empty leaves the password unchanged.</summary>
    public string? NewPassword { get; init; }

    public IReadOnlyList<string> Roles { get; init; } = [];

    public bool CanCreateShares { get; init; } = true;

    public bool CanUseApi { get; init; }

    public IReadOnlyList<int> AccessGroupIds { get; init; } = [];
}

public record RoleOption(string Name, string? Description);

public record ServerSettingsDto
{
    public string Theme { get; init; } = Constants.ServerSettingDefaults.Theme;

    public string SiteName { get; init; } = "ArcaFS";

    public bool SetupComplete { get; init; }

    public bool AllowRegistration { get; init; }

    public bool SharesEnabled { get; init; } = true;

    public FileOpenTarget OpenFilesIn { get; init; } = FileOpenTarget.Dialog;
}

/// <summary>Per-user preferences that live on the Identity row rather than in localStorage.</summary>
public record UserPreferencesDto
{
    public string? PreferredTheme { get; init; }

    public ListingViewMode PreferredViewMode { get; init; } = ListingViewMode.Normal;

    public bool ShowHiddenFiles { get; init; }

    public bool SingleClickToOpen { get; init; }
}

public record ActivityEventDto(
    long Id,
    ActivityKind Kind,
    string? UserId,
    string? UserName,
    int? SourceId,
    string? SourceName,
    string? Path,
    string? TargetPath,
    string? ShareToken,
    long? SizeBytes,
    bool Succeeded,
    string? Detail,
    string? IpAddress,
    DateTime OccurredAt);

public record ActivityQueryRequest
{
    public ActivityKind? Kind { get; init; }

    public string? UserId { get; init; }

    public int? SourceId { get; init; }

    public DateTime? FromUtc { get; init; }

    public DateTime? ToUtc { get; init; }

    /// <summary>True shows only successes, false only failures, null everything.</summary>
    public bool? SucceededOnly { get; init; }

    /// <summary>Substring match against the recorded path.</summary>
    public string? PathContains { get; init; }

    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 100;
}

/// <summary>
/// One page of results plus the totals needed to render a pager. Named <c>PagedList</c> rather
/// than <c>PagedResult</c> because Ardalis.Result ships a type by the latter name and the
/// services project has both namespaces globally imported.
/// </summary>
public record PagedList<T>(IReadOnlyList<T> Items, int TotalCount, int PageNumber, int PageSize)
{
    public int PageCount => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < PageCount;

    public static PagedList<T> Empty(int pageSize = 100) => new([], 0, 1, pageSize);
}

public record ApiTokenDto(
    int Id,
    string Name,
    string TokenPrefix,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    DateTime? LastUsedAt,
    bool IsRevoked);

/// <summary>
/// Returned once, at creation. <see cref="Token"/> is not recoverable afterwards because only
/// its hash is stored.
/// </summary>
public record CreatedApiTokenDto(ApiTokenDto Info, string Token);

public record FavoriteDto(
    int Id,
    int SourceId,
    string SourceName,
    string Path,
    string Label,
    bool IsDirectory);

/// <summary>Dashboard tiles on the home page.</summary>
public record DashboardStatsDto(
    int SourceCount,
    long IndexedItemCount,
    long IndexedSizeBytes,
    int UserCount,
    int ActiveShareCount,
    int AccessRuleCount,
    DateTime? LastIndexedAt,
    IReadOnlyList<SourceSummaryDto> Sources,
    IReadOnlyList<ActivityEventDto> RecentActivity);