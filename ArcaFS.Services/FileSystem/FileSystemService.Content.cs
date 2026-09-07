using System.IO.Compression;
using System.Text;

namespace ArcaFS.Services.FileSystem;

public sealed partial class FileSystemService
{
    public async Task<Result<TextFileContentDto>> ReadTextAsync(
        int sourceId,
        string path,
        CancellationToken cancellationToken = default)
    {
        var resolved = await permissionService.RequireAsync(
            sourceId,
            path,
            FilePermission.Read,
            cancellationToken);

        if (!resolved.IsSuccess)
        {
            return resolved.To<TextFileContentDto>();
        }

        var target = resolved.Value;

        if (!File.Exists(target.PhysicalPath))
        {
            return Result<TextFileContentDto>.NotFound($"'{path}' was not found.");
        }

        var info = new FileInfo(target.PhysicalPath);

        if (info.Length > MaxEditableFileBytes)
        {
            return Result<TextFileContentDto>.Invalid(new ValidationError(
                nameof(path),
                $"This file is {FileKinds.FormatSize(info.Length)}. The editor opens files up to " +
                $"{FileKinds.FormatSize(MaxEditableFileBytes)}."));
        }

        // A file's extension is a hint, not a guarantee. Sniffing for NUL bytes stops the editor
        // opening a binary as mojibake and then writing that back over the original.
        if (await LooksBinaryAsync(target.PhysicalPath, cancellationToken))
        {
            return Result<TextFileContentDto>.Invalid(new ValidationError(
                nameof(path), "This file appears to be binary, so it cannot be edited as text."));
        }

        string content;
        try
        {
            content = await File.ReadAllTextAsync(target.PhysicalPath, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Could not read text from {Physical}", target.PhysicalPath);
            return Result<TextFileContentDto>.Error("That file could not be read.");
        }

        return Result<TextFileContentDto>.Success(new TextFileContentDto(
            target.ScopedVirtualPath,
            info.Name,
            content,
            FileKinds.GetEditorLanguage(info.Name),
            info.Length,
            info.LastWriteTimeUtc,
            target.Permissions.Has(FilePermission.Modify)));
    }

    public async Task<Result> SaveTextAsync(
        SaveTextFileRequest request,
        CancellationToken cancellationToken = default)
    {
        var resolved = await permissionService.RequireAsync(
            request.SourceId,
            request.Path,
            FilePermission.Modify,
            cancellationToken);

        if (!resolved.IsSuccess)
        {
            return resolved.ToVoid();
        }

        var target = resolved.Value;

        if (!File.Exists(target.PhysicalPath))
        {
            return Result.NotFound($"'{request.Path}' was not found.");
        }

        try
        {
            // Written to a sibling temp file and swapped in, so a failure part-way through
            // leaves the original intact rather than a half-saved file.
            string temporary = target.PhysicalPath + Constants.UploadTempSuffix;

            await File.WriteAllTextAsync(temporary, request.Content, new UTF8Encoding(false), cancellationToken);
            File.Move(temporary, target.PhysicalPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Could not save text to {Physical}", target.PhysicalPath);
            return Result.Error("That file could not be saved.");
        }

        await activityService.RecordAsync(
            ActivityKind.Modify,
            request.SourceId,
            target.ScopedVirtualPath,
            sizeBytes: Encoding.UTF8.GetByteCount(request.Content),
            cancellationToken: cancellationToken);

        await InvalidateIndexAsync(request.SourceId, target.AbsoluteVirtualPath, cancellationToken);

        return Result.Success();
    }

    public async Task<Result<FileDownloadDto>> OpenReadAsync(
        int sourceId,
        string path,
        AccessPrincipal? principal = null,
        CancellationToken cancellationToken = default)
    {
        var required = FilePermission.Read | FilePermission.Download;

        var resolved = principal is null
            ? await permissionService.RequireAsync(sourceId, path, required, cancellationToken)
            : await permissionService.RequireAsync(sourceId, path, required, principal, cancellationToken);

        if (!resolved.IsSuccess)
        {
            return resolved.To<FileDownloadDto>();
        }

        var target = resolved.Value;

        if (Directory.Exists(target.PhysicalPath))
        {
            return Result<FileDownloadDto>.Invalid(new ValidationError(
                nameof(path), "That is a folder. Download it as an archive instead."));
        }

        if (!File.Exists(target.PhysicalPath))
        {
            return Result<FileDownloadDto>.NotFound($"'{path}' was not found.");
        }

        var info = new FileInfo(target.PhysicalPath);

        FileStream stream;
        try
        {
            // Async and sequential: this stream is copied straight to the response body, and
            // FileShare.Read lets other readers in while a large download is in flight.
            stream = new FileStream(
                target.PhysicalPath,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.Read,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Could not open {Physical} for download", target.PhysicalPath);
            return Result<FileDownloadDto>.Error("That file could not be opened.");
        }

        await activityService.RecordAsync(
            ActivityKind.Download,
            sourceId,
            target.ScopedVirtualPath,
            sizeBytes: info.Length,
            overrideUserId: principal?.UserId,
            cancellationToken: cancellationToken);

        return Result<FileDownloadDto>.Success(new FileDownloadDto(
            stream,
            info.Name,
            FileKinds.GetMimeType(info.Name),
            info.Length,
            info.LastWriteTimeUtc,
            BuildETag(info)));
    }

    public async Task<Result> WriteZipAsync(
        int sourceId,
        IReadOnlyList<string> paths,
        Stream destination,
        AccessPrincipal? principal = null,
        CancellationToken cancellationToken = default)
    {
        if (paths.Count == 0)
        {
            return Result.Invalid(new ValidationError(nameof(paths), "Select at least one item."));
        }

        var required = FilePermission.Read | FilePermission.Download;
        var targets = new List<ResolvedPath>(paths.Count);

        // Everything is permission-checked before a single byte is written, because once the
        // response body has started we can no longer turn the download into an error page.
        foreach (string path in paths)
        {
            var resolved = principal is null
                ? await permissionService.RequireAsync(sourceId, path, required, cancellationToken)
                : await permissionService.RequireAsync(sourceId, path, required, principal, cancellationToken);

            if (!resolved.IsSuccess)
            {
                return resolved.ToVoid();
            }

            targets.Add(resolved.Value);
        }

        long totalBytes = 0;

        try
        {
            // leaveOpen: the caller owns the response stream and will finish it.
            using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);

            foreach (var target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (Directory.Exists(target.PhysicalPath))
                {
                    string rootName = Path.GetFileName(target.PhysicalPath);
                    totalBytes += await AddDirectoryToZipAsync(
                        archive,
                        target,
                        rootName,
                        cancellationToken);
                }
                else if (File.Exists(target.PhysicalPath))
                {
                    totalBytes += await AddFileToZipAsync(
                        archive,
                        target.PhysicalPath,
                        Path.GetFileName(target.PhysicalPath),
                        cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The user navigated away or cancelled the download. Not an error worth logging.
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed while writing a zip of {Count} item(s)", paths.Count);
            return Result.Error("The archive could not be created.");
        }

        await activityService.RecordAsync(
            ActivityKind.Download,
            sourceId,
            targets[0].ScopedVirtualPath,
            sizeBytes: totalBytes,
            detail: paths.Count == 1 ? "Folder as zip" : $"{paths.Count} items as zip",
            overrideUserId: principal?.UserId,
            cancellationToken: cancellationToken);

        return Result.Success();
    }

    /// <summary>
    /// Adds a directory tree to the archive, skipping entries the caller has no rule permitting
    /// them to read. A folder download must not become a way around a deny rule on a file inside it.
    /// </summary>
    private async Task<long> AddDirectoryToZipAsync(
        ZipArchive archive,
        ResolvedPath root,
        string entryPrefix,
        CancellationToken cancellationToken)
    {
        var principal = await permissionService.GetCurrentPrincipalAsync(cancellationToken);
        var evaluate = await permissionService.GetChildPermissionEvaluatorAsync(
            root.Source.Id,
            root.ScopedVirtualPath,
            principal,
            cancellationToken);

        var excludes = ExcludeMatcher.For(root.Source);
        long bytes = 0;

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = root.Source.FollowSymlinks
                ? FileAttributes.None
                : FileAttributes.ReparsePoint,
        };

        foreach (var file in new DirectoryInfo(root.PhysicalPath).EnumerateFiles("*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (excludes.IsExcluded(file.Name)
                || file.Name.EndsWith(Constants.UploadTempSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            string relative = Path.GetRelativePath(root.PhysicalPath, file.FullName)
                .Replace(Path.DirectorySeparatorChar, Constants.PathSeparator);

            string childVirtual = VirtualPath.Combine(root.ScopedVirtualPath, relative.Split(Constants.PathSeparator));

            if (!evaluate(childVirtual).Has(FilePermission.Read))
            {
                continue;
            }

            bytes += await AddFileToZipAsync(
                archive,
                file.FullName,
                $"{entryPrefix}/{relative}",
                cancellationToken);
        }

        return bytes;
    }

    private async Task<long> AddFileToZipAsync(
        ZipArchive archive,
        string physicalPath,
        string entryName,
        CancellationToken cancellationToken)
    {
        try
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            entry.LastWriteTime = File.GetLastWriteTime(physicalPath);

            await using var entryStream = entry.Open();
            await using var fileStream = new FileStream(
                physicalPath,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.Read,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                });

            await fileStream.CopyToAsync(entryStream, cancellationToken);

            return fileStream.Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // One locked file shouldn't abort a whole folder download.
            logger.LogWarning(ex, "Skipped {Physical} while building an archive", physicalPath);
            return 0;
        }
    }

    public async Task<Result<UploadResultDto>> UploadAsync(
        UploadRequest request,
        AccessPrincipal? principal = null,
        CancellationToken cancellationToken = default)
    {
        string name = VirtualPath.SanitizeSegment(request.FileName);
        if (name.Length == 0)
        {
            return Result<UploadResultDto>.Invalid(new ValidationError(
                nameof(request.FileName), "The upload has no usable file name."));
        }

        var required = request.Overwrite
            ? FilePermission.Create | FilePermission.Modify
            : FilePermission.Create;

        var resolved = principal is null
            ? await permissionService.RequireAsync(
                request.SourceId,
                request.Path,
                required,
                cancellationToken)
            : await permissionService.RequireAsync(
                request.SourceId,
                request.Path,
                required,
                principal,
                cancellationToken);

        if (!resolved.IsSuccess)
        {
            await activityService.RecordAsync(
                ActivityKind.PermissionDenied,
                request.SourceId,
                VirtualPath.Combine(VirtualPath.Normalize(request.Path), name),
                succeeded: false,
                detail: "Upload",
                cancellationToken: cancellationToken);

            return resolved.To<UploadResultDto>();
        }

        var target = resolved.Value;

        if (!Directory.Exists(target.PhysicalPath))
        {
            return Result<UploadResultDto>.NotFound($"'{request.Path}' was not found.");
        }

        string finalPath = Path.Combine(target.PhysicalPath, name);
        string virtualPath = VirtualPath.Combine(target.ScopedVirtualPath, name);

        if (!request.Overwrite && File.Exists(finalPath) && request.ChunkOffset == 0)
        {
            return Result<UploadResultDto>.Conflict($"'{name}' already exists here.");
        }

        // Chunks accumulate in a temp file named after the session, so two users uploading
        // different files of the same name at once can't corrupt each other's transfer.
        string temporaryPath = request.UploadSession is null
            ? finalPath + Constants.UploadTempSuffix
            : Path.Combine(
                target.PhysicalPath,
                $"{name}.{VirtualPath.SanitizeSegment(request.UploadSession)}{Constants.UploadTempSuffix}");

        long received;

        try
        {
            received = await AppendChunkAsync(temporaryPath, request, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Upload of {Name} to {Physical} failed", name, target.PhysicalPath);
            TryDelete(temporaryPath);

            return Result<UploadResultDto>.Error($"'{name}' could not be uploaded.");
        }

        bool isComplete = request.TotalSize is null or <= 0 || received >= request.TotalSize;

        if (!isComplete)
        {
            return Result<UploadResultDto>.Success(
                new UploadResultDto(virtualPath, name, received, IsComplete: false));
        }

        try
        {
            File.Move(temporaryPath, finalPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Could not finalise upload of {Name} at {Physical}", name, finalPath);
            TryDelete(temporaryPath);

            return Result<UploadResultDto>.Error($"'{name}' could not be saved.");
        }

        await activityService.RecordAsync(
            ActivityKind.Upload,
            request.SourceId,
            virtualPath,
            sizeBytes: received,
            cancellationToken: cancellationToken);

        await InvalidateIndexAsync(request.SourceId, target.AbsoluteVirtualPath, cancellationToken);

        return Result<UploadResultDto>.Success(
            new UploadResultDto(virtualPath, name, received, IsComplete: true));
    }

    /// <summary>
    /// Writes one chunk at its declared offset and returns the temp file's resulting length.
    /// Seeking rather than appending makes a retried chunk idempotent, so a client that resends
    /// after a dropped connection doesn't duplicate bytes.
    /// </summary>
    private static async Task<long> AppendChunkAsync(
        string temporaryPath,
        UploadRequest request,
        CancellationToken cancellationToken)
    {
        await using var file = new FileStream(
            temporaryPath,
            new FileStreamOptions
            {
                Mode = request.ChunkOffset == 0 ? FileMode.Create : FileMode.OpenOrCreate,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous,
            });

        if (request.ChunkOffset > 0)
        {
            file.Seek(request.ChunkOffset, SeekOrigin.Begin);
        }

        await request.Content.CopyToAsync(file, cancellationToken);
        await file.FlushAsync(cancellationToken);

        return file.Length;
    }

    private void TryDelete(string physicalPath)
    {
        try
        {
            if (File.Exists(physicalPath))
            {
                File.Delete(physicalPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not remove temporary file {Physical}", physicalPath);
        }
    }

    /// <summary>
    /// Treats a file as binary if the first 8 KB contain a NUL byte — the same heuristic git
    /// uses, and reliable enough to keep the editor away from executables and media.
    /// </summary>
    private static async Task<bool> LooksBinaryAsync(string physicalPath, CancellationToken cancellationToken)
    {
        const int SampleSize = 8 * 1024;

        await using var stream = new FileStream(
            physicalPath,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });

        byte[] buffer = new byte[SampleSize];
        int read = await stream.ReadAsync(buffer.AsMemory(0, SampleSize), cancellationToken);

        return buffer.AsSpan(0, read).Contains((byte)0);
    }

    /// <summary>
    /// Opaque validator over size and modification time. Cheap, and changes whenever the file
    /// does, which is all a conditional GET needs. The HTTP layer quotes this and marks it weak;
    /// the value itself must stay unquoted so it is a legal ETag name.
    /// </summary>
    private static string BuildETag(FileInfo info) =>
        $"{info.Length:x}-{info.LastWriteTimeUtc.Ticks:x}";
}
