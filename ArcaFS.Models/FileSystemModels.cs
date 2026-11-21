namespace ArcaFS.Models;

/// <summary>One entry in a directory listing.</summary>
public record FileSystemItemDto(
    string Name,

    /// <summary>Virtual path relative to the caller's scope, so a scoped user never sees the prefix.</summary>
    string Path,
    bool IsDirectory,
    long SizeBytes,
    DateTime ModifiedAt,
    FileKind Kind,
    string? Extension,
    bool IsHidden,
    bool IsSymlink,

    /// <summary>Immediate child count for directories from the index; null when unknown.</summary>
    int? ChildCount,

    /// <summary>True when a thumbnail can be generated, so the tile can lazily request one.</summary>
    bool HasThumbnail);

/// <summary>
/// A directory listing plus everything the UI needs to render its chrome: breadcrumbs, the
/// caller's effective permissions here, and paging state.
/// </summary>
public record DirectoryListingDto(
    int SourceId,
    string SourceName,
    string Path,
    IReadOnlyList<FileSystemItemDto> Items,
    IReadOnlyList<BreadcrumbDto> Breadcrumbs,

    /// <summary>What the caller may do at <paramref name="Path"/>. Drives which actions render.</summary>
    FilePermission Permissions,
    int TotalCount,
    int PageNumber,
    int PageSize,
    long TotalSizeBytes,
    bool IsSourceReadOnly);

public record BreadcrumbDto(string Name, string Path);

/// <summary>Metadata for a single item, backing the info panel and the preview router.</summary>
public record FileDetailDto(
    int SourceId,
    string Path,
    string Name,
    bool IsDirectory,
    long SizeBytes,
    DateTime ModifiedAt,
    DateTime CreatedAt,
    FileKind Kind,
    string MimeType,
    FilePermission Permissions,

    /// <summary>Populated for images once the file has been probed.</summary>
    int? ImageWidth,
    int? ImageHeight);

public record CreateDirectoryRequest
{
    public int SourceId { get; init; }

    /// <summary>Parent directory the new folder is created in.</summary>
    public string Path { get; init; } = Constants.RootPath;

    public string Name { get; init; } = string.Empty;
}

public record RenameRequest
{
    public int SourceId { get; init; }

    public string Path { get; init; } = string.Empty;

    public string NewName { get; init; } = string.Empty;
}

/// <summary>
/// A move or copy. The source and destination can be in different sources, in which case both
/// ends are permission-checked independently (Move/Download on the origin, Create on the target).
/// </summary>
public record TransferRequest
{
    public int SourceId { get; init; }

    public IReadOnlyList<string> Paths { get; init; } = [];

    public int DestinationSourceId { get; init; }

    public string DestinationPath { get; init; } = Constants.RootPath;

    public bool IsCopy { get; init; }

    /// <summary>Overwrite existing destination entries. Requires <see cref="FilePermission.Modify"/>.</summary>
    public bool Overwrite { get; init; }

    /// <summary>
    /// When the destination name is taken and <see cref="Overwrite"/> is false, write beside it
    /// as "name (2)" instead of reporting a conflict. The caller already asked the user.
    /// </summary>
    public bool RenameOnConflict { get; init; }
}

public record DeleteRequest
{
    public int SourceId { get; init; }

    public IReadOnlyList<string> Paths { get; init; } = [];
}

public record SaveTextFileRequest
{
    public int SourceId { get; init; }

    public string Path { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// The modified time the editor observed when it opened the file. When set, a save is refused
    /// if the file changed in between, so two people don't silently overwrite each other.
    /// Leave it null when creating a file, or when an undo is writing bytes it just verified.
    /// </summary>
    public DateTime? ExpectedModifiedAt { get; init; }
}

/// <summary>Where a text save landed, and the modified time to send with the next save.</summary>
public sealed record TextSaveDto(string Path, string Name, DateTime ModifiedAt);

public record CreateArchiveRequest
{
    public int SourceId { get; init; }

    /// <summary>Directory the archive is written into.</summary>
    public string Path { get; init; } = Constants.RootPath;

    public IReadOnlyList<string> Paths { get; init; } = [];

    public string ArchiveName { get; init; } = string.Empty;

    /// <summary>One of <c>zip</c>, <c>tar</c>, <c>tar.gz</c>.</summary>
    public string Format { get; init; } = "zip";

    public bool DeleteSourcesAfter { get; init; }
}

public record ExtractArchiveRequest
{
    public int SourceId { get; init; }

    public string Path { get; init; } = string.Empty;

    /// <summary>Null extracts into the archive's parent folder (the current folder).</summary>
    public string? DestinationPath { get; init; }

    public bool Overwrite { get; init; }

    /// <summary>
    /// Per top-level name inside the archive. Empty keeps the previous behaviour: any collision
    /// refuses the extract before a byte is written.
    /// </summary>
    public IReadOnlyList<ExtractNameChoice> NameChoices { get; init; } = [];
}

/// <summary>One colliding top-level archive entry, and whether replace is safe.</summary>
public sealed record ExtractConflictDto(string Name, bool AllowReplace);

/// <summary>The user's decision for one colliding top-level name.</summary>
public sealed record ExtractNameChoice(string Name, NameConflictChoice Choice);

/// <summary>Listing query state — kept in the URL so a folder view is linkable.</summary>
public record ListingRequest
{
    public int SourceId { get; init; }

    public string Path { get; init; } = Constants.RootPath;

    public ListingSortField SortBy { get; init; } = ListingSortField.Name;

    public bool Descending { get; init; }

    public bool ShowHidden { get; init; }

    /// <summary>Type-ahead filter applied to the current directory only.</summary>
    public string? Filter { get; init; }

    /// <summary>Type chip. Folders are always included so navigation still works.</summary>
    public KindFilter Kind { get; init; } = KindFilter.All;

    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 500;
}

/// <summary>
/// Per-item outcome for a bulk operation. Bulk actions are deliberately partial-success: one
/// undeletable file shouldn't abort the other forty-nine.
/// </summary>
public record FileOperationResultDto(
    string Path,
    bool Succeeded,
    string? Error,
    string? ResultPath = null,
    bool NameConflict = false);

public record BulkOperationResultDto(
    int SucceededCount,
    int FailedCount,
    IReadOnlyList<FileOperationResultDto> Results)
{
    public bool IsCompleteSuccess => FailedCount == 0;

    public static BulkOperationResultDto From(IEnumerable<FileOperationResultDto> results)
    {
        var list = results.ToList();
        return new BulkOperationResultDto(
            list.Count(r => r.Succeeded),
            list.Count(r => !r.Succeeded),
            list);
    }
}

/// <summary>Recursive totals for a directory, filled in asynchronously by the info panel.</summary>
public record DirectorySizeDto(
    string Path,
    long SizeBytes,
    int FileCount,
    int DirectoryCount);

/// <summary>A text file loaded into the editor.</summary>
public record TextFileContentDto(
    string Path,
    string Name,
    string Content,

    /// <summary>Hint for the editor's syntax mode, derived from the extension.</summary>
    string Language,
    long SizeBytes,
    DateTime ModifiedAt,

    /// <summary>False when the caller may read but not write, so the editor opens read-only.</summary>
    bool CanSave);

/// <summary>
/// Everything needed to serve a file response. <see cref="Stream"/> is owned by the caller and
/// must be disposed once written.
/// </summary>
public record FileDownloadDto(
    Stream Stream,
    string FileName,
    string MimeType,
    long SizeBytes,
    DateTime ModifiedAt,

    /// <summary>Weak ETag derived from size and modification time, for conditional requests.</summary>
    string ETag)
{
    /// <summary>Inline for media and documents the browser renders; attachment for the rest.</summary>
    public bool IsInlineByDefault => FileKinds.IsInlineSafe(MimeType);
}

public record UploadRequest
{
    public int SourceId { get; init; }

    /// <summary>Destination directory.</summary>
    public string Path { get; init; } = Constants.RootPath;

    public string FileName { get; init; } = string.Empty;

    /// <summary>Read to completion by the service; not disposed by it.</summary>
    public Stream Content { get; init; } = Stream.Null;

    /// <summary>Byte offset this chunk starts at. Zero for a single-shot upload.</summary>
    public long ChunkOffset { get; init; }

    /// <summary>Total size of the finished file, used to detect the final chunk.</summary>
    public long? TotalSize { get; init; }

    /// <summary>Correlates chunks of one file. Null means a single-shot upload.</summary>
    public string? UploadSession { get; init; }

    public bool Overwrite { get; init; }
}

public record UploadResultDto(
    string Path,
    string Name,
    long BytesReceived,

    /// <summary>False while chunks are still outstanding.</summary>
    bool IsComplete);

/// <summary>One entry inside an archive, listed without extracting it.</summary>
public record ArchiveEntryDto(
    string Path,
    string Name,
    bool IsDirectory,
    long SizeBytes,
    long CompressedSizeBytes,
    DateTime? ModifiedAt,
    FileKind Kind);