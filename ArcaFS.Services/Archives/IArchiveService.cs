namespace ArcaFS.Services.Archives;

/// <summary>Creating archives from a selection, and extracting them back onto a source.</summary>
public interface IArchiveService
{
    Task<Result<FileSystemItemDto>> CreateAsync(
        CreateArchiveRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<BulkOperationResultDto>> ExtractAsync(
        ExtractArchiveRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Top-level names in the archive that would land on something already in the destination.
    /// The extract itself still refuses those names until the caller sends a choice for each.
    /// </summary>
    Task<Result<IReadOnlyList<ExtractConflictDto>>> GetExtractConflictsAsync(
        int sourceId,
        string path,
        string? destinationPath = null,
        CancellationToken cancellationToken = default);

    /// <summary>Lists an archive's contents without extracting it, for the preview panel.</summary>
    Task<Result<IReadOnlyList<ArchiveEntryDto>>> ListEntriesAsync(
        int sourceId,
        string path,
        CancellationToken cancellationToken = default);
}