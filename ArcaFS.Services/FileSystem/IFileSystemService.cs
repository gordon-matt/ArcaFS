namespace ArcaFS.Services.FileSystem;

/// <summary>
/// Browsing and mutating operations over a source's contents. Every method resolves its paths
/// through <see cref="IPermissionService"/> first, so a caller cannot reach a byte it has no
/// rule granting it.
/// </summary>
public interface IFileSystemService
{
    /// <param name="principal">
    /// Explicit principal for a public share visit, where there is no signed-in caller. Null
    /// resolves the current user.
    /// </param>
    Task<Result<DirectoryListingDto>> ListAsync(
        ListingRequest request,
        AccessPrincipal? principal = null,
        CancellationToken cancellationToken = default);

    Task<Result<FileDetailDto>> GetDetailAsync(
        int sourceId,
        string path,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recursive size and item count for a directory, computed on demand. Separate from
    /// <see cref="GetDetailAsync"/> because walking a large tree is too slow for the info panel's
    /// first paint.
    /// </summary>
    Task<Result<DirectorySizeDto>> GetDirectorySizeAsync(
        int sourceId,
        string path,
        CancellationToken cancellationToken = default);

    Task<Result<FileSystemItemDto>> CreateDirectoryAsync(
        CreateDirectoryRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<FileSystemItemDto>> RenameAsync(
        RenameRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Moves or copies entries, possibly across sources. Partial success is normal.</summary>
    Task<Result<BulkOperationResultDto>> TransferAsync(
        TransferRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<BulkOperationResultDto>> DeleteAsync(
        DeleteRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a text file for the built-in editor, refusing anything oversized or binary.</summary>
    Task<Result<TextFileContentDto>> ReadTextAsync(
        int sourceId,
        string path,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes text. Creates the file when it does not exist yet. When
    /// <see cref="SaveTextFileRequest.ExpectedModifiedAt"/> is set, refuses the save if the file
    /// changed after the editor opened it.
    /// </summary>
    Task<Result<TextSaveDto>> SaveTextAsync(
        SaveTextFileRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a file for download. The caller owns the returned stream. Records a Download
    /// activity event.
    /// </summary>
    Task<Result<FileDownloadDto>> OpenReadAsync(
        int sourceId,
        string path,
        AccessPrincipal? principal = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams a set of entries as a zip, written directly to <paramref name="destination"/>
    /// so a multi-gigabyte selection never lands in memory or a temp file.
    /// </summary>
    Task<Result> WriteZipAsync(
        int sourceId,
        IReadOnlyList<string> paths,
        Stream destination,
        AccessPrincipal? principal = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Accepts an upload, optionally as one chunk of a resumable transfer. Writes to a temp file
    /// and moves it into place only once complete, so a failed upload never leaves a
    /// truncated file that looks real.
    /// </summary>
    Task<Result<UploadResultDto>> UploadAsync(
        UploadRequest request,
        AccessPrincipal? principal = null,
        CancellationToken cancellationToken = default);
}