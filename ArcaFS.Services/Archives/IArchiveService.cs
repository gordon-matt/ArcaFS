namespace ArcaFS.Services.Archives;

/// <summary>Creating archives from a selection, and extracting them back onto a source.</summary>
public interface IArchiveService
{
    /// <summary>
    /// Writes a zip, tar, or tar.gz of the selected items into the destination folder.
    /// Creating it requires create permission there. Reading each member requires read and archive.
    /// </summary>
    /// <param name="request">Destination folder, members, format, and whether to delete the members afterward.</param>
    /// <param name="cancellationToken">Cancels the permission checks and the archive write.</param>
    /// <returns>
    /// The new archive, or a failure when the selection is empty, the format is unknown, the
    /// name is taken, or a member cannot be read. A partial archive is deleted.
    /// </returns>
    Task<Result<FileSystemItemDto>> CreateAsync(
        CreateArchiveRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts an archive into a folder. Entries that would escape the destination are refused.
    /// Extraction stops once 50 GiB or 200,000 entries have been written.
    /// </summary>
    /// <param name="request">
    /// Archive path, optional destination, and a choice for each conflicting top-level name.
    /// </param>
    /// <param name="cancellationToken">Cancels the permission checks and the extract.</param>
    /// <returns>
    /// One outcome per extracted name, or a failure when the archive is missing, the caller
    /// cannot extract it, or a top-level name conflicts and no choice was sent.
    /// </returns>
    Task<Result<BulkOperationResultDto>> ExtractAsync(
        ExtractArchiveRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Top-level names in the archive that would land on something already in the destination.
    /// The extract itself still refuses those names until the caller sends a choice for each.
    /// </summary>
    /// <param name="sourceId">Source that contains the archive.</param>
    /// <param name="path">Archive path as the caller sees it.</param>
    /// <param name="destinationPath">
    /// Folder the names would be extracted into. <see langword="null"/> uses the archive's parent.
    /// </param>
    /// <param name="cancellationToken">Cancels the permission check and the archive listing.</param>
    /// <returns>The conflicting top-level names, or a failure when the archive cannot be read.</returns>
    Task<Result<IReadOnlyList<ExtractConflictDto>>> GetExtractConflictsAsync(
        int sourceId,
        string path,
        string? destinationPath = null,
        CancellationToken cancellationToken = default);

    /// <summary>Lists an archive's contents without extracting it, for the preview panel.</summary>
    /// <param name="sourceId">Source that contains the archive.</param>
    /// <param name="path">Archive path as the caller sees it.</param>
    /// <param name="cancellationToken">Cancels the permission check and the listing.</param>
    /// <returns>The entries, or a failure when the file is missing or is not a readable archive.</returns>
    Task<Result<IReadOnlyList<ArchiveEntryDto>>> ListEntriesAsync(
        int sourceId,
        string path,
        CancellationToken cancellationToken = default);
}
