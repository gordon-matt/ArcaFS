namespace ArcaFS.Models;

/// <summary>Outcome of one index scan, logged and shown on the sources admin page.</summary>
public record IndexScanResultDto(
    int SourceId,
    long FileCount,
    long DirectoryCount,
    long TotalSizeBytes,
    int RemovedCount,
    TimeSpan Elapsed);

/// <summary>
/// A search over the index. <see cref="Query"/> carries the raw user text including any
/// <c>type:</c> filters; the parsed form lands in <see cref="SearchQuery"/>.
/// </summary>
public record SearchRequest
{
    public string Query { get; init; } = string.Empty;

    /// <summary>Null searches every source the caller can browse.</summary>
    public int? SourceId { get; init; }

    /// <summary>Restricts results to this subtree.</summary>
    public string? Path { get; init; }

    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 100;
}

/// <summary>
/// The structured form of a search string. Supports <c>type:image</c>,
/// <c>type:largerThan=50M</c>, <c>type:newerThan=2024-01-01</c>, <c>case:exact</c>,
/// quoted phrases, and <c>a|b</c> alternation.
/// </summary>
public record SearchQuery
{
    /// <summary>Terms to match against the file name. Any match qualifies (OR).</summary>
    public IReadOnlyList<string> Terms { get; init; } = [];

    /// <summary>Require every term instead of any.</summary>
    public bool RequireAllTerms { get; init; }

    /// <summary>Terms came from a quoted phrase, so match the literal substring.</summary>
    public bool ExactPhrase { get; init; }

    public bool CaseSensitive { get; init; }

    public IReadOnlyList<FileKind> Kinds { get; init; } = [];

    public bool? FoldersOnly { get; init; }

    public long? LargerThanBytes { get; init; }

    public long? SmallerThanBytes { get; init; }

    public DateTime? NewerThanUtc { get; init; }

    public DateTime? OlderThanUtc { get; init; }

    /// <summary>True when nothing usable was supplied, so callers can skip the query entirely.</summary>
    public bool IsEmpty => Terms.Count == 0
        && Kinds.Count == 0
        && FoldersOnly is null
        && LargerThanBytes is null
        && SmallerThanBytes is null
        && NewerThanUtc is null
        && OlderThanUtc is null;
}

public record SearchResultItemDto(
    int SourceId,
    string SourceName,
    string Path,
    string Name,
    bool IsDirectory,
    long SizeBytes,
    DateTime ModifiedAt,
    FileKind Kind);

public record SearchResultsDto(
    string Query,
    IReadOnlyList<SearchResultItemDto> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,

    /// <summary>Set when the index hasn't finished its first pass, so results may be incomplete.</summary>
    string? Warning);

/// <summary>A set of files sharing a content hash, from the duplicate finder tool.</summary>
public record DuplicateGroupDto(
    string ContentHash,
    long SizeBytes,
    IReadOnlyList<SearchResultItemDto> Items)
{
    /// <summary>Bytes reclaimable by keeping one copy.</summary>
    public long WastedBytes => SizeBytes * Math.Max(0, Items.Count - 1);
}

/// <summary>One row of the disk-usage treemap: a child folder and its recursive size.</summary>
public record DiskUsageEntryDto(
    string Name,
    string Path,
    bool IsDirectory,
    long SizeBytes,
    double Percentage);

public record DiskUsageDto(
    int SourceId,
    string Path,
    long TotalSizeBytes,
    IReadOnlyList<DiskUsageEntryDto> Entries);
