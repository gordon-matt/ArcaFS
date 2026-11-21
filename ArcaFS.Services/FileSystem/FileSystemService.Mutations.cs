namespace ArcaFS.Services.FileSystem;

public sealed partial class FileSystemService
{
    public async Task<Result<FileSystemItemDto>> CreateDirectoryAsync(
        CreateDirectoryRequest request,
        CancellationToken cancellationToken = default)
    {
        string name = VirtualPath.SanitizeSegment(request.Name);
        if (name.Length == 0)
        {
            return Result<FileSystemItemDto>.Invalid(new ValidationError(
                nameof(request.Name), "Enter a folder name."));
        }

        // Create is checked on the parent: the folder being created doesn't exist yet, so there
        // is nothing at its own path to hold a rule.
        var parent = await permissionService.RequireAsync(
            request.SourceId,
            request.Path,
            FilePermission.Create,
            cancellationToken);

        if (!parent.IsSuccess)
        {
            return parent.To<FileSystemItemDto>();
        }

        string physical = Path.Combine(parent.Value.PhysicalPath, name);

        if (Directory.Exists(physical) || File.Exists(physical))
        {
            return Result<FileSystemItemDto>.Conflict($"'{name}' already exists here.");
        }

        try
        {
            Directory.CreateDirectory(physical);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Could not create directory {Physical}", physical);
            return Result<FileSystemItemDto>.Error($"'{name}' could not be created.");
        }

        string virtualPath = VirtualPath.Combine(parent.Value.ScopedVirtualPath, name);

        await activityService.RecordAsync(
            ActivityKind.Create,
            request.SourceId,
            virtualPath,
            cancellationToken: cancellationToken);

        var info = new DirectoryInfo(physical);

        return Result<FileSystemItemDto>.Success(
            ToItem(info, virtualPath, parent.Value.Permissions, videoThumbnails.IsAvailable));
    }

    public async Task<Result<FileSystemItemDto>> RenameAsync(
        RenameRequest request,
        CancellationToken cancellationToken = default)
    {
        string newName = VirtualPath.SanitizeSegment(request.NewName);
        if (newName.Length == 0)
        {
            return Result<FileSystemItemDto>.Invalid(new ValidationError(
                nameof(request.NewName), "Enter a name."));
        }

        var resolved = await permissionService.RequireAsync(
            request.SourceId,
            request.Path,
            FilePermission.Rename,
            cancellationToken);

        if (!resolved.IsSuccess)
        {
            return resolved.To<FileSystemItemDto>();
        }

        var target = resolved.Value;
        bool isDirectory = Directory.Exists(target.PhysicalPath);

        if (!isDirectory && !File.Exists(target.PhysicalPath))
        {
            return Result<FileSystemItemDto>.NotFound($"'{request.Path}' was not found.");
        }

        string? parentPhysical = Path.GetDirectoryName(target.PhysicalPath);
        if (parentPhysical is null)
        {
            return Result<FileSystemItemDto>.Invalid(new ValidationError(
                nameof(request.Path), "A source root cannot be renamed."));
        }

        string destination = Path.Combine(parentPhysical, newName);

        // A case-only rename on Windows and macOS looks like a collision against itself, so let
        // it through rather than reporting a bogus conflict.
        bool isCaseOnlyChange = string.Equals(
            destination,
            target.PhysicalPath,
            StringComparison.OrdinalIgnoreCase);

        if (!isCaseOnlyChange && (Directory.Exists(destination) || File.Exists(destination)))
        {
            return Result<FileSystemItemDto>.Conflict($"'{newName}' already exists here.");
        }

        try
        {
            if (isDirectory)
            {
                Directory.Move(target.PhysicalPath, destination);
            }
            else
            {
                File.Move(target.PhysicalPath, destination, overwrite: false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Could not rename {Physical} to {NewName}", target.PhysicalPath, newName);
            return Result<FileSystemItemDto>.Error($"'{request.Path}' could not be renamed.");
        }

        string? parentVirtual = VirtualPath.GetParent(target.ScopedVirtualPath) ?? Constants.RootPath;
        string newVirtualPath = VirtualPath.Combine(parentVirtual, newName);

        await activityService.RecordAsync(
            ActivityKind.Rename,
            request.SourceId,
            target.ScopedVirtualPath,
            targetPath: newVirtualPath,
            cancellationToken: cancellationToken);

        await InvalidateIndexAsync(request.SourceId, target.AbsoluteVirtualPath, cancellationToken);

        FileSystemInfo renamed = isDirectory
            ? new DirectoryInfo(destination)
            : new FileInfo(destination);

        return Result<FileSystemItemDto>.Success(
            ToItem(renamed, newVirtualPath, target.Permissions, videoThumbnails.IsAvailable));
    }

    public async Task<Result<BulkOperationResultDto>> DeleteAsync(
        DeleteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Paths.Count == 0)
        {
            return Result<BulkOperationResultDto>.Invalid(new ValidationError(
                nameof(request.Paths), "Select at least one item."));
        }

        var results = new List<FileOperationResultDto>(request.Paths.Count);

        foreach (string path in request.Paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var resolved = await permissionService.RequireAsync(
                request.SourceId,
                path,
                FilePermission.Delete,
                cancellationToken);

            if (!resolved.IsSuccess)
            {
                results.Add(new FileOperationResultDto(path, false, resolved.GetDisplayMessage()));

                await activityService.RecordAsync(
                    ActivityKind.PermissionDenied,
                    request.SourceId,
                    path,
                    succeeded: false,
                    detail: "Delete",
                    cancellationToken: cancellationToken);

                continue;
            }

            var target = resolved.Value;

            try
            {
                bool isDirectory = Directory.Exists(target.PhysicalPath);
                long size = isDirectory ? 0 : new FileInfo(target.PhysicalPath).Length;

                if (isDirectory)
                {
                    Directory.Delete(target.PhysicalPath, recursive: true);
                }
                else if (File.Exists(target.PhysicalPath))
                {
                    File.Delete(target.PhysicalPath);
                }
                else
                {
                    results.Add(new FileOperationResultDto(path, false, "No longer exists."));
                    continue;
                }

                results.Add(new FileOperationResultDto(path, true, null));

                await activityService.RecordAsync(
                    ActivityKind.Delete,
                    request.SourceId,
                    target.ScopedVirtualPath,
                    sizeBytes: size == 0 ? null : size,
                    cancellationToken: cancellationToken);

                await InvalidateIndexAsync(request.SourceId, target.AbsoluteVirtualPath, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogError(ex, "Could not delete {Physical}", target.PhysicalPath);
                results.Add(new FileOperationResultDto(path, false, "Could not be deleted."));
            }
        }

        return Result<BulkOperationResultDto>.Success(BulkOperationResultDto.From(results));
    }

    public async Task<Result<BulkOperationResultDto>> TransferAsync(
        TransferRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Paths.Count == 0)
        {
            return Result<BulkOperationResultDto>.Invalid(new ValidationError(
                nameof(request.Paths), "Select at least one item."));
        }

        // A copy only needs to read the origin; a move has to remove it afterwards.
        var originRequirement = request.IsCopy
            ? FilePermission.Read
            : FilePermission.Move | FilePermission.Delete;

        var destination = await permissionService.RequireAsync(
            request.DestinationSourceId,
            request.DestinationPath,
            request.Overwrite ? FilePermission.Create | FilePermission.Modify : FilePermission.Create,
            cancellationToken);

        if (!destination.IsSuccess)
        {
            return destination.To<BulkOperationResultDto>();
        }

        var destinationTarget = destination.Value;

        if (!Directory.Exists(destinationTarget.PhysicalPath))
        {
            return Result<BulkOperationResultDto>.NotFound(
                $"The destination '{request.DestinationPath}' was not found.");
        }

        var results = new List<FileOperationResultDto>(request.Paths.Count);

        foreach (string path in request.Paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var origin = await permissionService.RequireAsync(
                request.SourceId,
                path,
                originRequirement,
                cancellationToken);

            if (!origin.IsSuccess)
            {
                results.Add(new FileOperationResultDto(path, false, origin.GetDisplayMessage()));
                continue;
            }

            var result = await TransferOneAsync(
                origin.Value,
                destinationTarget,
                request,
                cancellationToken);

            results.Add(result);
        }

        return Result<BulkOperationResultDto>.Success(BulkOperationResultDto.From(results));
    }

    private async Task<FileOperationResultDto> TransferOneAsync(
        ResolvedPath origin,
        ResolvedPath destination,
        TransferRequest request,
        CancellationToken cancellationToken)
    {
        string name = Path.GetFileName(origin.PhysicalPath);
        bool isDirectory = Directory.Exists(origin.PhysicalPath);

        if (!isDirectory && !File.Exists(origin.PhysicalPath))
        {
            return new FileOperationResultDto(origin.ScopedVirtualPath, false, "No longer exists.");
        }

        string finalPath = Path.Combine(destination.PhysicalPath, name);

        // Copying a folder into itself or into its own subtree would recurse until the disk
        // filled. Moving a folder into its own subtree is equally impossible. Both are easy to
        // trigger by dragging in the UI, so they are rejected before any bytes move.
        if (isDirectory && IsWithin(finalPath, origin.PhysicalPath))
        {
            return new FileOperationResultDto(
                origin.ScopedVirtualPath,
                false,
                "A folder cannot be moved or copied inside itself.");
        }

        if (PathsEqual(origin.PhysicalPath, finalPath))
        {
            return request.IsCopy
                ? await CopyBesideAsync(origin, destination, cancellationToken)
                : new FileOperationResultDto(origin.ScopedVirtualPath, true, null, origin.ScopedVirtualPath);
        }

        bool destinationIsDirectory = Directory.Exists(finalPath);
        bool exists = destinationIsDirectory || File.Exists(finalPath);
        if (exists && destinationIsDirectory != isDirectory)
        {
            return new FileOperationResultDto(
                origin.ScopedVirtualPath,
                false,
                $"'{name}' already exists here as a {(destinationIsDirectory ? "folder" : "file")}.");
        }

        if (exists && !request.Overwrite)
        {
            if (!request.RenameOnConflict)
            {
                return new FileOperationResultDto(
                    origin.ScopedVirtualPath,
                    false,
                    $"'{name}' already exists in the destination.",
                    NameConflict: true);
            }

            name = FreeNames.Find(destination.PhysicalPath, name, isDirectory);
            finalPath = Path.Combine(destination.PhysicalPath, name);
        }

        try
        {
            if (request.IsCopy)
            {
                if (isDirectory)
                {
                    CopyDirectory(origin.PhysicalPath, finalPath, request.Overwrite);
                }
                else
                {
                    File.Copy(origin.PhysicalPath, finalPath, request.Overwrite);
                }
            }
            else
            {
                MoveAcrossVolumes(origin.PhysicalPath, finalPath, isDirectory, request.Overwrite);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(
                ex,
                "Could not {Operation} {From} to {To}",
                request.IsCopy ? "copy" : "move",
                origin.PhysicalPath,
                finalPath);

            return new FileOperationResultDto(
                origin.ScopedVirtualPath,
                false,
                request.IsCopy ? "Could not be copied." : "Could not be moved.");
        }

        string destinationVirtual = VirtualPath.Combine(destination.ScopedVirtualPath, Path.GetFileName(finalPath));

        await activityService.RecordAsync(
            request.IsCopy ? ActivityKind.Copy : ActivityKind.Move,
            request.SourceId,
            origin.ScopedVirtualPath,
            targetPath: destinationVirtual,
            detail: request.DestinationSourceId == request.SourceId
                ? null
                : $"To source '{destination.Source.Name}'",
            cancellationToken: cancellationToken);

        if (!request.IsCopy)
        {
            await InvalidateIndexAsync(request.SourceId, origin.AbsoluteVirtualPath, cancellationToken);
        }

        return new FileOperationResultDto(origin.ScopedVirtualPath, true, null, destinationVirtual);
    }

    /// <summary>
    /// Handles "copy here" onto the item's own folder by giving the copy a free name, the way a
    /// desktop file manager does, instead of refusing the operation.
    /// </summary>
    private async Task<FileOperationResultDto> CopyBesideAsync(
        ResolvedPath origin,
        ResolvedPath destination,
        CancellationToken cancellationToken)
    {
        bool isDirectory = Directory.Exists(origin.PhysicalPath);
        string name = Path.GetFileName(origin.PhysicalPath);
        string unique = FreeNames.Find(destination.PhysicalPath, name, isDirectory);
        string finalPath = Path.Combine(destination.PhysicalPath, unique);

        try
        {
            if (isDirectory)
            {
                CopyDirectory(origin.PhysicalPath, finalPath, overwrite: false);
            }
            else
            {
                File.Copy(origin.PhysicalPath, finalPath, overwrite: false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Could not copy {Physical} beside itself", origin.PhysicalPath);
            return new FileOperationResultDto(origin.ScopedVirtualPath, false, "Could not be copied.");
        }

        string resultPath = VirtualPath.Combine(destination.ScopedVirtualPath, unique);
        await activityService.RecordAsync(
            ActivityKind.Copy,
            origin.Source.Id,
            origin.ScopedVirtualPath,
            targetPath: resultPath,
            cancellationToken: cancellationToken);

        return new FileOperationResultDto(origin.ScopedVirtualPath, true, null, resultPath);
    }

    /// <summary>
    /// <see cref="File.Move(string, string, bool)"/> and <see cref="Directory.Move"/> fail across
    /// volumes, which is the normal case when moving between two sources on different mounts.
    /// Falls back to copy-then-delete so the move still succeeds.
    /// </summary>
    private static void MoveAcrossVolumes(string from, string to, bool isDirectory, bool overwrite)
    {
        try
        {
            if (isDirectory)
            {
                if (overwrite && Directory.Exists(to))
                {
                    Directory.Delete(to, recursive: true);
                }

                Directory.Move(from, to);
            }
            else
            {
                File.Move(from, to, overwrite);
            }

            return;
        }
        catch (IOException)
        {
            // Fall through to copy-and-delete.
        }

        if (isDirectory)
        {
            CopyDirectory(from, to, overwrite);
            Directory.Delete(from, recursive: true);
        }
        else
        {
            File.Copy(from, to, overwrite);
            File.Delete(from);
        }
    }

    private static void CopyDirectory(string from, string to, bool overwrite)
    {
        Directory.CreateDirectory(to);

        foreach (string file in Directory.EnumerateFiles(from))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite);
        }

        foreach (string directory in Directory.EnumerateDirectories(from))
        {
            CopyDirectory(directory, Path.Combine(to, Path.GetFileName(directory)), overwrite);
        }
    }

    /// <summary>
    /// Whether <paramref name="candidate"/> sits at or below <paramref name="ancestor"/> on disk.
    /// Compared on full paths with a trailing separator so <c>/data/foo2</c> is not treated as a
    /// child of <c>/data/foo</c>.
    /// </summary>
    private static bool IsWithin(string candidate, string ancestor)
    {
        string normalizedCandidate = Path.GetFullPath(candidate);
        string normalizedAncestor = Path.GetFullPath(ancestor);

        if (PathsEqual(normalizedCandidate, normalizedAncestor))
        {
            return true;
        }

        string prefix = normalizedAncestor.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedAncestor
            : normalizedAncestor + Path.DirectorySeparatorChar;

        return normalizedCandidate.StartsWith(prefix, PathComparison);
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), PathComparison);

    /// <summary>
    /// Windows and macOS default to case-insensitive filesystems; Linux does not. Using the
    /// wrong comparison would either miss a real collision or invent one.
    /// </summary>
    private static StringComparison PathComparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>
    /// Drops index rows for a path and everything beneath it after a destructive change, so
    /// search results don't point at files that are gone. The next scan refills them.
    /// </summary>
    private async Task InvalidateIndexAsync(
        int sourceId,
        string absoluteVirtualPath,
        CancellationToken cancellationToken)
    {
        try
        {
            string prefix = absoluteVirtualPath == Constants.RootPath
                ? Constants.RootPath
                : absoluteVirtualPath + Constants.PathSeparator;

            var stale = await indexRepository.FindAsync(new SearchOptions<IndexedItem>
            {
                Query = i => i.SourceId == sourceId
                    && (i.Path == absoluteVirtualPath || i.Path.StartsWith(prefix)),
                CancellationToken = cancellationToken,
            });

            var list = stale.ToList();
            if (list.Count > 0)
            {
                await indexRepository.DeleteAsync(list);
            }
        }
        catch (Exception ex)
        {
            // The index is derived data; a stale entry is a cosmetic problem, and failing the
            // user's delete because we couldn't tidy up would not be.
            logger.LogWarning(
                ex,
                "Could not invalidate index rows under {Path} in source {SourceId}",
                absoluteVirtualPath,
                sourceId);
        }
    }
}