namespace ArcaFS.Services.FileSystem;

/// <summary>
/// The filesystem gateway. Two rules hold throughout this class:
/// every path arrives as an untrusted virtual path and leaves
/// <see cref="IPermissionService.RequireAsync(int, string, FilePermission, CancellationToken)"/>
/// as a verified physical one; and no operation is attempted before its permission is checked.
/// </summary>
public sealed partial class FileSystemService(
    ILogger<FileSystemService> logger,
    IPermissionService permissionService,
    IActivityService activityService,
    IRepository<IndexedItem> indexRepository) : IFileSystemService
{
    /// <summary>
    /// Ceiling on files opened in the text editor. Large enough for any config or log excerpt,
    /// small enough that a stray 2 GB file can't exhaust the server's memory.
    /// </summary>
    private const long MaxEditableFileBytes = 5 * 1024 * 1024;

    public async Task<Result<DirectoryListingDto>> ListAsync(
        ListingRequest request,
        AccessPrincipal? explicitPrincipal = null,
        CancellationToken cancellationToken = default)
    {
        var resolved = explicitPrincipal is null
            ? await permissionService.RequireAsync(
                request.SourceId,
                request.Path,
                FilePermission.Browse,
                cancellationToken)
            : await permissionService.RequireAsync(
                request.SourceId,
                request.Path,
                FilePermission.Browse,
                explicitPrincipal,
                cancellationToken);

        if (!resolved.IsSuccess)
        {
            return resolved.To<DirectoryListingDto>();
        }

        var target = resolved.Value;

        if (!Directory.Exists(target.PhysicalPath))
        {
            return File.Exists(target.PhysicalPath)
                ? Result<DirectoryListingDto>.Invalid(new ValidationError(
                    nameof(request.Path), "That path is a file, not a folder."))
                : Result<DirectoryListingDto>.NotFound($"'{request.Path}' was not found.");
        }

        var principal = explicitPrincipal
            ?? await permissionService.GetCurrentPrincipalAsync(cancellationToken);

        // One evaluator for the whole directory: the source's rules get loaded and narrowed to
        // this subtree once instead of per child.
        var evaluate = await permissionService.GetChildPermissionEvaluatorAsync(
            request.SourceId,
            target.AbsoluteVirtualPath,
            principal,
            cancellationToken);

        var excludes = ExcludeMatcher.For(target.Source);
        var items = new List<FileSystemItemDto>();
        long totalSize = 0;

        try
        {
            var directory = new DirectoryInfo(target.PhysicalPath);

            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (ShouldSkip(entry, target.Source, request.ShowHidden, excludes))
                {
                    continue;
                }

                string scopedChild = VirtualPath.Combine(target.ScopedVirtualPath, entry.Name);
                string absoluteChild = VirtualPath.Combine(target.AbsoluteVirtualPath, entry.Name);

                // A child the caller can neither browse nor read is omitted entirely rather than
                // shown-but-locked: leaking the names of files you can't open is itself a leak.
                var childPermissions = evaluate(absoluteChild);
                if (!childPermissions.HasAny(FilePermission.Browse | FilePermission.Read))
                {
                    continue;
                }

                var item = ToItem(entry, scopedChild, childPermissions);
                items.Add(item);
                totalSize += item.SizeBytes;
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            // The OS disagreed with our ACL. Almost always a misconfigured source directory.
            logger.LogWarning(
                ex,
                "Denied by the operating system while listing {Physical} in source {SourceId}",
                target.PhysicalPath,
                target.Source.Id);

            return Result<DirectoryListingDto>.Error(
                "The server does not have permission to read that folder.");
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "I/O failure listing {Physical}", target.PhysicalPath);
            return Result<DirectoryListingDto>.Error("That folder could not be read.");
        }

        var filtered = ApplyFilter(items, request.Filter);
        int totalCount = filtered.Count;

        var page = Sort(filtered, request.SortBy, request.Descending)
            .Skip((Math.Max(1, request.PageNumber) - 1) * Math.Max(1, request.PageSize))
            .Take(Math.Max(1, request.PageSize))
            .ToList();

        await PopulateChildCountsAsync(target, page, cancellationToken);

        return Result<DirectoryListingDto>.Success(new DirectoryListingDto(
            target.Source.Id,
            target.Source.Name,
            target.ScopedVirtualPath,
            page,
            BuildBreadcrumbs(target.Source.Name, target.ScopedVirtualPath),
            target.Permissions,
            totalCount,
            Math.Max(1, request.PageNumber),
            Math.Max(1, request.PageSize),
            totalSize,
            target.Source.IsReadOnly));
    }

    public async Task<Result<FileDetailDto>> GetDetailAsync(
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
            return resolved.To<FileDetailDto>();
        }

        var target = resolved.Value;
        bool isDirectory = Directory.Exists(target.PhysicalPath);

        if (!isDirectory && !File.Exists(target.PhysicalPath))
        {
            return Result<FileDetailDto>.NotFound($"'{path}' was not found.");
        }

        var info = isDirectory
            ? new DirectoryInfo(target.PhysicalPath)
            : (FileSystemInfo)new FileInfo(target.PhysicalPath);

        var kind = FileKinds.Classify(info.Name, isDirectory);
        long size = info is FileInfo file ? file.Length : 0;

        (int? width, int? height) = kind == FileKind.Image
            ? await ImageProbe.TryGetDimensionsAsync(target.PhysicalPath, logger, cancellationToken)
            : (null, null);

        return Result<FileDetailDto>.Success(new FileDetailDto(
            target.Source.Id,
            target.ScopedVirtualPath,
            info.Name,
            isDirectory,
            size,
            info.LastWriteTimeUtc,
            info.CreationTimeUtc,
            kind,
            isDirectory ? "inode/directory" : FileKinds.GetMimeType(info.Name),
            target.Permissions,
            width,
            height));
    }

    public async Task<Result<DirectorySizeDto>> GetDirectorySizeAsync(
        int sourceId,
        string path,
        CancellationToken cancellationToken = default)
    {
        var resolved = await permissionService.RequireAsync(
            sourceId,
            path,
            FilePermission.Browse,
            cancellationToken);

        if (!resolved.IsSuccess)
        {
            return resolved.To<DirectorySizeDto>();
        }

        var target = resolved.Value;

        if (!Directory.Exists(target.PhysicalPath))
        {
            return Result<DirectorySizeDto>.NotFound($"'{path}' was not found.");
        }

        long bytes = 0;
        int files = 0;
        int directories = 0;

        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = target.Source.FollowSymlinks
                    ? FileAttributes.None
                    : FileAttributes.ReparsePoint,
            };

            foreach (var entry in new DirectoryInfo(target.PhysicalPath)
                .EnumerateFileSystemInfos("*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (entry is FileInfo file)
                {
                    bytes += file.Length;
                    files++;
                }
                else
                {
                    directories++;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Partial walk computing size of {Physical}", target.PhysicalPath);
        }

        return Result<DirectorySizeDto>.Success(
            new DirectorySizeDto(target.ScopedVirtualPath, bytes, files, directories));
    }

    private static IReadOnlyList<BreadcrumbDto> BuildBreadcrumbs(string sourceName, string virtualPath)
    {
        var crumbs = new List<BreadcrumbDto> { new(sourceName, Constants.RootPath) };

        string accumulated = string.Empty;
        foreach (string segment in VirtualPath.GetSegments(virtualPath))
        {
            accumulated = $"{accumulated}/{segment}";
            crumbs.Add(new BreadcrumbDto(segment, accumulated));
        }

        return crumbs;
    }

    private static FileSystemItemDto ToItem(
        FileSystemInfo entry,
        string virtualPath,
        FilePermission permissions)
    {
        bool isDirectory = entry is DirectoryInfo;
        long size = entry is FileInfo file ? file.Length : 0;
        var kind = FileKinds.Classify(entry.Name, isDirectory);

        return new FileSystemItemDto(
            entry.Name,
            virtualPath,
            isDirectory,
            size,
            entry.LastWriteTimeUtc,
            kind,
            isDirectory ? null : entry.Extension,
            IsHidden(entry),
            entry.LinkTarget is not null,
            ChildCount: null,
            HasThumbnail: kind == FileKind.Image && permissions.Has(FilePermission.Read));
    }

    private static bool IsHidden(FileSystemInfo entry) =>
        entry.Name.StartsWith('.') || entry.Attributes.HasFlag(FileAttributes.Hidden);

    private static bool ShouldSkip(
        FileSystemInfo entry,
        Source source,
        bool showHidden,
        ExcludeMatcher excludes) =>
        // In-progress uploads are real files on disk but not yet real content.
        entry.Name.EndsWith(Constants.UploadTempSuffix, StringComparison.Ordinal) || (!source.FollowSymlinks && entry.LinkTarget is not null) || (!showHidden && source.IgnoreHiddenFiles && IsHidden(entry)) || excludes.IsExcluded(entry.Name);

    private static List<FileSystemItemDto> ApplyFilter(List<FileSystemItemDto> items, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return items;
        }

        string term = filter.Trim();
        return items
            .Where(i => i.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static IEnumerable<FileSystemItemDto> Sort(
        IEnumerable<FileSystemItemDto> items,
        ListingSortField sortBy,
        bool descending)
    {
        // Directories lead regardless of the sort field, matching every file manager.
        var ordered = items.OrderBy(i => i.IsDirectory ? 0 : 1);

        return sortBy switch
        {
            ListingSortField.Size => descending
                ? ordered.ThenByDescending(i => i.SizeBytes)
                : ordered.ThenBy(i => i.SizeBytes),

            ListingSortField.Modified => descending
                ? ordered.ThenByDescending(i => i.ModifiedAt)
                : ordered.ThenBy(i => i.ModifiedAt),

            ListingSortField.Kind => descending
                ? ordered.ThenByDescending(i => i.Kind).ThenBy(i => i.Name, NaturalComparer.Instance)
                : ordered.ThenBy(i => i.Kind).ThenBy(i => i.Name, NaturalComparer.Instance),

            _ => descending
                ? ordered.ThenByDescending(i => i.Name, NaturalComparer.Instance)
                : ordered.ThenBy(i => i.Name, NaturalComparer.Instance),
        };
    }

    /// <summary>
    /// Fills in <see cref="FileSystemItemDto.ChildCount"/> for directories from the index. Read
    /// from the index rather than the disk because stat-ing every subdirectory of a large folder
    /// costs more than the count is worth; an absent or stale index just leaves it null.
    /// </summary>
    private async Task PopulateChildCountsAsync(
        ResolvedPath target,
        List<FileSystemItemDto> page,
        CancellationToken cancellationToken)
    {
        if (!target.Source.IsIndexed)
        {
            return;
        }

        var directoryPaths = page
            .Where(i => i.IsDirectory)
            .Select(i => VirtualPath.Combine(target.Scope, i.Path))
            .ToList();

        if (directoryPaths.Count == 0)
        {
            return;
        }

        try
        {
            int sourceId = target.Source.Id;
            var rows = await indexRepository.FindAsync(new SearchOptions<IndexedItem>
            {
                Query = i => i.SourceId == sourceId
                    && i.IsDirectory
                    && directoryPaths.Contains(i.Path),
                CancellationToken = cancellationToken,
            });

            var counts = rows.ToDictionary(
                r => r.Path,
                r => r.ChildCount,
                StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < page.Count; i++)
            {
                if (!page[i].IsDirectory)
                {
                    continue;
                }

                string absolute = VirtualPath.Combine(target.Scope, page[i].Path);
                if (counts.TryGetValue(absolute, out int count))
                {
                    page[i] = page[i] with { ChildCount = count };
                }
            }
        }
        catch (Exception ex)
        {
            // Cosmetic data. Never fail a listing over it.
            logger.LogDebug(ex, "Could not read child counts for source {SourceId}", target.Source.Id);
        }
    }
}