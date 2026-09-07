namespace ArcaFS.Enums;

/// <summary>Listing layouts offered by the file browser.</summary>
public enum ListingViewMode
{
    /// <summary>Medium tiles with a thumbnail. The default.</summary>
    Normal = 0,

    /// <summary>One row per item with name / size / modified columns.</summary>
    List = 1,

    /// <summary>Large thumbnail tiles.</summary>
    Grid = 2,

    /// <summary>Dense single-line rows, no thumbnails.</summary>
    Compact = 3,

    /// <summary>Edge-to-edge image grid for photo folders.</summary>
    Gallery = 4,
}

/// <summary>Where a double-clicked file opens.</summary>
public enum FileOpenTarget
{
    /// <summary>Overlay on top of the folder listing. The default.</summary>
    Dialog = 0,

    /// <summary>A dedicated page, the way the text editor used to work.</summary>
    Page = 1,
}

public enum ListingSortField
{
    Name = 0,
    Size = 1,
    Modified = 2,
    Kind = 3,
}

/// <summary>Broad file classification driving icon, preview mode and <c>type:</c> search filters.</summary>
public enum FileKind
{
    Other = 0,
    Folder = 1,
    Image = 2,
    Video = 3,
    Audio = 4,
    Document = 5,
    Archive = 6,
    Text = 7,
    Code = 8,
    Pdf = 9,
    Epub = 10,
}

/// <summary>What a share link permits. Maps onto a <see cref="FilePermission"/> mask at check time.</summary>
public enum ShareMode
{
    /// <summary>View and download only.</summary>
    ReadOnly = 0,

    /// <summary>Recipients may also upload into the shared folder.</summary>
    Upload = 1,

    /// <summary>Recipients may upload, overwrite, rename and delete.</summary>
    ReadWrite = 2,
}

/// <summary>Outcome recorded on an <c>ActivityEvent</c> row.</summary>
public enum ActivityKind
{
    SignIn = 0,
    SignOut = 1,
    Upload = 2,
    Download = 3,
    Create = 4,
    Modify = 5,
    Rename = 6,
    Move = 7,
    Copy = 8,
    Delete = 9,
    ShareCreated = 10,
    ShareAccessed = 11,
    PermissionDenied = 12,
    IndexScan = 13,
}

public static class ActivityKindExtensions
{
    /// <summary>Human-readable label for the activity log's Action column.</summary>
    public static string GetDisplayName(this ActivityKind kind) => kind switch
    {
        ActivityKind.SignIn => "Sign in",
        ActivityKind.SignOut => "Sign out",
        ActivityKind.ShareCreated => "Share created",
        ActivityKind.ShareAccessed => "Share accessed",
        ActivityKind.PermissionDenied => "Permission denied",
        ActivityKind.IndexScan => "Index scan",
        _ => kind.ToString(),
    };

    /// <summary>Bootstrap contextual colour used for the badge in the activity log.</summary>
    public static string GetBadgeClass(this ActivityKind kind) => kind switch
    {
        ActivityKind.Delete => "danger",
        ActivityKind.PermissionDenied => "danger",
        ActivityKind.Upload or ActivityKind.Create => "success",
        ActivityKind.Modify or ActivityKind.Rename or ActivityKind.Move => "warning",
        ActivityKind.ShareCreated or ActivityKind.ShareAccessed => "info",
        _ => "secondary",
    };
}
