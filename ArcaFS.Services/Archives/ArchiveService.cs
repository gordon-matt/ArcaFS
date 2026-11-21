using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Writers;

namespace ArcaFS.Services.Archives;

public sealed class ArchiveService(
    ILogger<ArchiveService> logger,
    IPermissionService permissionService,
    IActivityService activityService,
    IFileSystemService fileSystemService) : IArchiveService
{
    /// <summary>
    /// Refuses to extract an archive whose contents exceed this, and refuses to keep going once
    /// this much has been written. A small archive that expands to terabytes is the classic
    /// decompression bomb, and the only real defence is a hard ceiling.
    /// </summary>
    private const long MaxExtractedBytes = 50L * 1024 * 1024 * 1024;

    /// <summary>
    /// Companion limit on entry count, since a bomb can also be millions of empty files rather
    /// than a few enormous ones.
    /// </summary>
    private const int MaxExtractedEntries = 200_000;

    public async Task<Result<FileSystemItemDto>> CreateAsync(
        CreateArchiveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Paths.Count == 0)
        {
            return Result<FileSystemItemDto>.Invalid(new ValidationError(
                nameof(request.Paths), "Select at least one item to compress."));
        }

        var format = ResolveFormat(request.Format);
        if (format is null)
        {
            return Result<FileSystemItemDto>.Invalid(new ValidationError(
                nameof(request.Format), $"'{request.Format}' is not a supported archive format."));
        }

        string archiveName = BuildArchiveName(request, format.Value.Extension);

        // Writing the archive needs Create in the destination; reading the members needs
        // Archive on each. Both are separate permissions so an operator can allow downloading
        // a folder as a zip without allowing archives to be written back into it.
        var destination = await permissionService.RequireAsync(
            request.SourceId,
            request.Path,
            FilePermission.Create,
            cancellationToken);

        if (!destination.IsSuccess)
        {
            return destination.To<FileSystemItemDto>();
        }

        var targets = new List<ResolvedPath>(request.Paths.Count);

        foreach (string path in request.Paths)
        {
            var resolved = await permissionService.RequireAsync(
                request.SourceId,
                path,
                FilePermission.Read | FilePermission.Archive,
                cancellationToken);

            if (!resolved.IsSuccess)
            {
                return resolved.To<FileSystemItemDto>();
            }

            targets.Add(resolved.Value);
        }

        string archivePhysical = Path.Combine(destination.Value.PhysicalPath, archiveName);

        if (File.Exists(archivePhysical) || Directory.Exists(archivePhysical))
        {
            return Result<FileSystemItemDto>.Conflict($"'{archiveName}' already exists here.");
        }

        try
        {
            await WriteArchiveAsync(archivePhysical, format.Value, targets, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not create archive {Physical}", archivePhysical);

            // A partial archive is worse than none: it looks like a valid download.
            TryDelete(archivePhysical);

            return Result<FileSystemItemDto>.Error("The archive could not be created.");
        }

        var info = new FileInfo(archivePhysical);
        string virtualPath = VirtualPath.Combine(destination.Value.ScopedVirtualPath, archiveName);

        await activityService.RecordAsync(
            ActivityKind.Create,
            request.SourceId,
            virtualPath,
            sizeBytes: info.Length,
            detail: $"Archived {request.Paths.Count} item(s) as {format.Value.Extension}",
            cancellationToken: cancellationToken);

        if (request.DeleteSourcesAfter)
        {
            var deletion = await fileSystemService.DeleteAsync(
                new DeleteRequest { SourceId = request.SourceId, Paths = request.Paths },
                cancellationToken);

            if (deletion.IsSuccess && !deletion.Value.IsCompleteSuccess)
            {
                logger.LogWarning(
                    "Archive {Name} was created but {Count} source item(s) could not be removed",
                    archiveName,
                    deletion.Value.FailedCount);
            }
        }

        return Result<FileSystemItemDto>.Success(new FileSystemItemDto(
            info.Name,
            virtualPath,
            false,
            info.Length,
            info.LastWriteTimeUtc,
            FileKind.Archive,
            info.Extension,
            false,
            false,
            null,
            false));
    }

    public async Task<Result<BulkOperationResultDto>> ExtractAsync(
        ExtractArchiveRequest request,
        CancellationToken cancellationToken = default)
    {
        var archive = await permissionService.RequireAsync(
            request.SourceId,
            request.Path,
            FilePermission.Read | FilePermission.Extract,
            cancellationToken);

        if (!archive.IsSuccess)
        {
            return archive.To<BulkOperationResultDto>();
        }

        if (!File.Exists(archive.Value.PhysicalPath))
        {
            return Result<BulkOperationResultDto>.NotFound($"'{request.Path}' was not found.");
        }

        string destinationVirtual = DefaultExtractDestination(
            archive.Value.ScopedVirtualPath,
            request.DestinationPath);

        var destination = await permissionService.RequireAsync(
            request.SourceId,
            destinationVirtual,
            request.Overwrite ? FilePermission.Create | FilePermission.Modify : FilePermission.Create,
            cancellationToken);

        if (!destination.IsSuccess)
        {
            return destination.To<BulkOperationResultDto>();
        }

        if (File.Exists(destination.Value.PhysicalPath))
        {
            return Result<BulkOperationResultDto>.Conflict(
                $"'{Path.GetFileName(destination.Value.PhysicalPath)}' already exists here.");
        }

        var results = new List<FileOperationResultDto>();
        Dictionary<string, string>? renamedTops = null;
        IReadOnlyDictionary<string, NameConflictChoice>? choices = null;

        try
        {
            // Closed before the extract opens the same file. On Windows the first handle would
            // otherwise keep the archive locked and the second open would fail.
            using (IArchive listing = ArchiveFactory.OpenArchive(
                archive.Value.PhysicalPath,
                ReaderOptions.ForFilePath))
            {
                var entries = listing.Entries
                    .Where(e => e.Key is not null)
                    .Select(e => (e.Key!, e.IsDirectory))
                    .ToList();

                if (request.NameChoices.Count == 0)
                {
                    if (!request.Overwrite)
                    {
                        IReadOnlyList<string> conflicts = FindConflictingExtractNames(
                            destination.Value.PhysicalPath,
                            entries);

                        if (conflicts.Count > 0)
                        {
                            return Result<BulkOperationResultDto>.Conflict(
                                FormatAlreadyExistsMessage(conflicts));
                        }
                    }
                }
                else
                {
                    choices = request.NameChoices
                        .GroupBy(choice => choice.Name, NameComparer)
                        .ToDictionary(group => group.Key, group => group.Last().Choice, NameComparer);
                    renamedTops = BuildRenamedTops(
                        destination.Value.PhysicalPath,
                        entries,
                        choices);
                }
            }

            Directory.CreateDirectory(destination.Value.PhysicalPath);

            results = await ExtractEntriesAsync(
                archive.Value.PhysicalPath,
                destination.Value.PhysicalPath,
                request.Overwrite,
                choices,
                renamedTops,
                cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            // Raised by our own limit checks below.
            logger.LogWarning(ex, "Refused to extract {Physical}", archive.Value.PhysicalPath);
            return Result<BulkOperationResultDto>.Invalid(new ValidationError(
                nameof(request.Path), ex.Message));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not extract {Physical}", archive.Value.PhysicalPath);
            return Result<BulkOperationResultDto>.Error(
                "That archive could not be extracted. It may be corrupt or use an unsupported format.");
        }

        await activityService.RecordAsync(
            ActivityKind.Create,
            request.SourceId,
            destination.Value.ScopedVirtualPath,
            targetPath: archive.Value.ScopedVirtualPath,
            detail: $"Extracted {results.Count(r => r.Succeeded)} entry(ies)",
            cancellationToken: cancellationToken);

        return Result<BulkOperationResultDto>.Success(BulkOperationResultDto.From(results));
    }

    public async Task<Result<IReadOnlyList<ExtractConflictDto>>> GetExtractConflictsAsync(
        int sourceId,
        string path,
        string? destinationPath = null,
        CancellationToken cancellationToken = default)
    {
        Result<ResolvedPath> archive = await permissionService.RequireAsync(
            sourceId,
            path,
            FilePermission.Read | FilePermission.Extract,
            cancellationToken);

        if (!archive.IsSuccess)
        {
            return archive.To<IReadOnlyList<ExtractConflictDto>>();
        }

        if (!File.Exists(archive.Value.PhysicalPath))
        {
            return Result<IReadOnlyList<ExtractConflictDto>>.NotFound($"'{path}' was not found.");
        }

        string destinationVirtual = DefaultExtractDestination(archive.Value.ScopedVirtualPath, destinationPath);
        Result<ResolvedPath> destination = await permissionService.RequireAsync(
            sourceId,
            destinationVirtual,
            FilePermission.Create,
            cancellationToken);

        if (!destination.IsSuccess)
        {
            return destination.To<IReadOnlyList<ExtractConflictDto>>();
        }

        if (File.Exists(destination.Value.PhysicalPath))
        {
            return Result<IReadOnlyList<ExtractConflictDto>>.Conflict(
                $"'{Path.GetFileName(destination.Value.PhysicalPath)}' already exists here.");
        }

        using var listing = ArchiveFactory.OpenArchive(archive.Value.PhysicalPath, ReaderOptions.ForFilePath);
        IReadOnlyList<ExtractConflictDto> conflicts = FindTopLevelExtractConflicts(
            destination.Value.PhysicalPath,
            listing.Entries.Where(entry => entry.Key is not null).Select(entry => (entry.Key!, entry.IsDirectory)));

        return Result<IReadOnlyList<ExtractConflictDto>>.Success(conflicts);
    }

    public async Task<Result<IReadOnlyList<ArchiveEntryDto>>> ListEntriesAsync(
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
            return resolved.To<IReadOnlyList<ArchiveEntryDto>>();
        }

        if (!File.Exists(resolved.Value.PhysicalPath))
        {
            return Result<IReadOnlyList<ArchiveEntryDto>>.NotFound($"'{path}' was not found.");
        }

        try
        {
            using var reader = ArchiveFactory.OpenArchive(
                resolved.Value.PhysicalPath,
                ReaderOptions.ForFilePath);

            IReadOnlyList<ArchiveEntryDto> entries = reader.Entries
                .Where(e => e.Key is not null)
                .Take(MaxExtractedEntries)
                .Select(e => new ArchiveEntryDto(
                    e.Key!,
                    Path.GetFileName(e.Key!.TrimEnd('/')),
                    e.IsDirectory,
                    e.Size,
                    e.CompressedSize,
                    e.LastModifiedTime?.ToUniversalTime(),
                    FileKinds.Classify(e.Key!, e.IsDirectory)))
                .ToList();

            return Result<IReadOnlyList<ArchiveEntryDto>>.Success(entries);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not read archive {Physical}", resolved.Value.PhysicalPath);
            return Result<IReadOnlyList<ArchiveEntryDto>>.Invalid(new ValidationError(
                nameof(path), "That file could not be read as an archive."));
        }
    }

    private static async Task WriteArchiveAsync(
        string archivePhysical,
        (ArchiveType Type, CompressionType Compression, string Extension) format,
        List<ResolvedPath> targets,
        CancellationToken cancellationToken)
    {
        await using var output = new FileStream(
            archivePhysical,
            new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous,
            });

        using var writer = WriterFactory.OpenWriter(
            output,
            format.Type,
            new WriterOptions(format.Compression) { LeaveStreamOpen = true });

        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Directory.Exists(target.PhysicalPath))
            {
                string rootName = Path.GetFileName(target.PhysicalPath);

                foreach (string file in Directory.EnumerateFiles(
                    target.PhysicalPath,
                    "*",
                    new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        AttributesToSkip = target.Source.FollowSymlinks
                            ? FileAttributes.None
                            : FileAttributes.ReparsePoint,
                    }))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string relative = Path.GetRelativePath(target.PhysicalPath, file)
                        .Replace(Path.DirectorySeparatorChar, '/');

                    writer.Write($"{rootName}/{relative}", file);
                }
            }
            else if (File.Exists(target.PhysicalPath))
            {
                writer.Write(Path.GetFileName(target.PhysicalPath), target.PhysicalPath);
            }
        }
    }

    /// <summary>
    /// Extracts entries one at a time, resolving each destination inside
    /// <paramref name="destinationRoot"/> and enforcing the size and count ceilings.
    /// </summary>
    private async Task<List<FileOperationResultDto>> ExtractEntriesAsync(
        string archivePhysical,
        string destinationRoot,
        bool overwrite,
        IReadOnlyDictionary<string, NameConflictChoice>? choices,
        IReadOnlyDictionary<string, string>? renamedTops,
        CancellationToken cancellationToken)
    {
        var results = new List<FileOperationResultDto>();
        long writtenBytes = 0;
        int entryCount = 0;

        await using var input = File.OpenRead(archivePhysical);
        using var reader = ReaderFactory.OpenReader(input, ReaderOptions.ForExternalStream);

        string normalizedRoot = Path.GetFullPath(destinationRoot);

        while (reader.MoveToNextEntry())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = reader.Entry;
            if (entry.Key is null)
            {
                continue;
            }

            if (++entryCount > MaxExtractedEntries)
            {
                throw new InvalidOperationException(
                    $"This archive contains more than {MaxExtractedEntries:N0} entries, so it was not extracted.");
            }

            // The entry name comes from the archive, which is attacker-controlled data. An entry
            // named "../../etc/cron.d/x" would otherwise write outside the destination — the
            // zip-slip vulnerability. Resolve, then verify containment.
            string? entryDestination = ResolveEntryDestination(normalizedRoot, entry.Key);

            if (entryDestination is null)
            {
                logger.LogWarning(
                    "Skipped archive entry '{Entry}' in {Archive}: it resolves outside the destination",
                    entry.Key,
                    archivePhysical);

                results.Add(new FileOperationResultDto(
                    entry.Key,
                    false,
                    "Skipped: this entry points outside the destination folder."));

                continue;
            }

            string? top = TopSegment(entry.Key);
            bool replaceThis = overwrite;
            string entryKey = entry.Key;
            if (choices is not null && top is not null && choices.TryGetValue(top, out NameConflictChoice choice))
            {
                if (choice is NameConflictChoice.Skip or NameConflictChoice.Cancel)
                {
                    continue;
                }

                replaceThis = choice == NameConflictChoice.Replace;
                if (choice == NameConflictChoice.Rename
                    && renamedTops is not null
                    && renamedTops.TryGetValue(top, out string? renamed))
                {
                    entryKey = RewriteTop(entry.Key, renamed);
                    entryDestination = ResolveEntryDestination(normalizedRoot, entryKey);
                    if (entryDestination is null)
                    {
                        results.Add(new FileOperationResultDto(entry.Key, false, "Skipped: the new name is not a safe path."));
                        continue;
                    }

                    replaceThis = false;
                }
            }

            if (entry.IsDirectory)
            {
                Directory.CreateDirectory(entryDestination);
                continue;
            }

            if (entry.Size > 0 && writtenBytes + entry.Size > MaxExtractedBytes)
            {
                throw new InvalidOperationException(
                    $"This archive expands to more than {FileKinds.FormatSize(MaxExtractedBytes)}, " +
                    "so it was not extracted.");
            }

            if (!replaceThis && (File.Exists(entryDestination) || Directory.Exists(entryDestination)))
            {
                results.Add(new FileOperationResultDto(entry.Key, false, "Already exists."));
                continue;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(entryDestination)!);

                reader.WriteEntryToFile(entryDestination, new ExtractionOptions
                {
                    Overwrite = replaceThis,

                    // Never honour an archive's stored permissions or timestamps blindly; a
                    // crafted archive could mark an extracted file executable.
                    PreserveFileTime = false,
                    PreserveAttributes = false,
                    ExtractFullPath = false,
                });

                writtenBytes += Math.Max(0, entry.Size);
                results.Add(new FileOperationResultDto(entry.Key, true, null));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Could not extract entry '{Entry}'", entry.Key);
                results.Add(new FileOperationResultDto(entry.Key, false, "Could not be written."));
            }
        }

        return results;
    }

    /// <summary>
    /// Extracts into the archive's parent folder unless a destination was supplied. Compressing
    /// a file and extracting it again should put that file back next to the archive, not inside
    /// a new folder named after it.
    /// </summary>
    internal static string DefaultExtractDestination(string archiveScopedPath, string? requestedDestination) => !string.IsNullOrWhiteSpace(requestedDestination)
            ? VirtualPath.Normalize(requestedDestination)
            : VirtualPath.GetParent(VirtualPath.Normalize(archiveScopedPath)) ?? Constants.RootPath;

    /// <summary>
    /// Names of archive entries that would overwrite something already on disk. Used to refuse
    /// the whole extract before any files are written.
    /// </summary>
    internal static IReadOnlyList<string> FindConflictingExtractNames(
        string destinationRoot,
        IEnumerable<(string Key, bool IsDirectory)> entries)
    {
        string normalizedRoot = Path.GetFullPath(destinationRoot);
        var names = new List<string>();
        var seen = new HashSet<string>(
            OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);

        foreach (var (key, isDirectory) in entries)
        {
            string? dest = ResolveEntryDestination(normalizedRoot, key);
            if (dest is null)
            {
                continue;
            }

            bool conflict = isDirectory
                ? File.Exists(dest)
                : File.Exists(dest) || Directory.Exists(dest);

            if (!conflict)
            {
                continue;
            }

            string name = Path.GetFileName(
                dest.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            if (name.Length == 0)
            {
                name = key;
            }

            if (seen.Add(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    internal static string FormatAlreadyExistsMessage(IReadOnlyList<string> names) => names.Count == 1 ? $"'{names[0]}' already exists here." : "One or more files already exist here.";

    private static StringComparer NameComparer =>
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// One row per top-level name that would land on an existing file or folder. Nested entries
    /// are asked about once, as the folder they sit in, which is the name the user can see.
    /// </summary>
    internal static IReadOnlyList<ExtractConflictDto> FindTopLevelExtractConflicts(
        string destinationRoot,
        IEnumerable<(string Key, bool IsDirectory)> entries)
    {
        string normalizedRoot = Path.GetFullPath(destinationRoot);
        var incomingIsDirectory = new Dictionary<string, bool>(NameComparer);

        foreach ((string key, bool isDirectory) in entries)
        {
            string? top = TopSegment(key);
            if (top is null)
            {
                continue;
            }

            bool asDirectory = isDirectory || key.Contains('/') || key.Contains('\\');
            incomingIsDirectory[top] = incomingIsDirectory.GetValueOrDefault(top) || asDirectory;
        }

        var conflicts = new List<ExtractConflictDto>();
        foreach ((string top, bool incomingDirectory) in incomingIsDirectory)
        {
            string? destination = ResolveEntryDestination(normalizedRoot, top);
            if (destination is null)
            {
                continue;
            }

            bool existingDirectory = Directory.Exists(destination);
            bool existingFile = File.Exists(destination);
            if (!existingDirectory && !existingFile)
            {
                continue;
            }

            conflicts.Add(new ExtractConflictDto(top, AllowReplace: existingDirectory == incomingDirectory));
        }

        return conflicts;
    }

    private static Dictionary<string, string> BuildRenamedTops(
        string destinationRoot,
        IEnumerable<(string Key, bool IsDirectory)> entries,
        IReadOnlyDictionary<string, NameConflictChoice> choices)
    {
        var incomingIsDirectory = new HashSet<string>(NameComparer);
        foreach ((string key, bool isDirectory) in entries)
        {
            string? top = TopSegment(key);
            if (top is not null && (isDirectory || key.Contains('/') || key.Contains('\\')))
            {
                incomingIsDirectory.Add(top);
            }
        }

        var renamed = new Dictionary<string, string>(NameComparer);
        foreach ((string name, NameConflictChoice choice) in choices)
        {
            if (choice != NameConflictChoice.Rename)
            {
                continue;
            }

            renamed[name] = FreeNames.Find(destinationRoot, name, incomingIsDirectory.Contains(name));
        }

        return renamed;
    }

    internal static string? TopSegment(string? entryKey)
    {
        if (string.IsNullOrWhiteSpace(entryKey))
        {
            return null;
        }

        string cleaned = entryKey.Replace('\\', '/').Trim().Trim('/');
        if (cleaned.Length == 0 || cleaned.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        int slash = cleaned.IndexOf('/');
        return slash < 0 ? cleaned : cleaned[..slash];
    }

    private static string RewriteTop(string entryKey, string newTop)
    {
        string cleaned = entryKey.Replace('\\', '/').Trim().TrimStart('/');
        int slash = cleaned.IndexOf('/');
        return slash < 0 ? newTop : newTop + cleaned[slash..];
    }

    /// <summary>
    /// Maps an archive entry name to a path inside <paramref name="destinationRoot"/>, or null
    /// when it cannot be made safe. Rejects absolute paths, drive letters and any traversal that
    /// survives normalisation.
    /// </summary>
    internal static string? ResolveEntryDestination(string destinationRoot, string entryKey)
    {
        string cleaned = entryKey.Replace('\\', '/').Trim();

        if (cleaned.Length == 0)
        {
            return null;
        }

        // A rooted entry name ("/etc/passwd", "C:\Windows\...") is never legitimate here.
        if (cleaned.StartsWith('/') || (cleaned.Length > 1 && cleaned[1] == ':'))
        {
            cleaned = cleaned.TrimStart('/');

            if (cleaned.Length > 1 && cleaned[1] == ':')
            {
                return null;
            }
        }

        var segments = new List<string>();

        foreach (string segment in cleaned.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                // Refuse rather than silently clamping: an entry trying to climb out is a sign
                // of a malicious archive, and reporting it is more useful than quietly
                // relocating the file.
                return null;
            }

            segments.Add(VirtualPath.SanitizeSegment(segment));
        }

        if (segments.Count == 0 || segments.Any(string.IsNullOrEmpty))
        {
            return null;
        }

        string candidate = Path.GetFullPath(Path.Combine([destinationRoot, .. segments]));

        // Belt and braces: even with the checks above, confirm the final path is contained.
        string prefix = destinationRoot.EndsWith(Path.DirectorySeparatorChar)
            ? destinationRoot
            : destinationRoot + Path.DirectorySeparatorChar;

        var comparison = OperatingSystem.IsLinux()
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        return candidate.StartsWith(prefix, comparison) ? candidate : null;
    }

    private static (ArchiveType Type, CompressionType Compression, string Extension)? ResolveFormat(
        string? format) =>
        format?.ToLowerInvariant() switch
        {
            "zip" or null or "" => (ArchiveType.Zip, CompressionType.Deflate, ".zip"),
            "tar" => (ArchiveType.Tar, CompressionType.None, ".tar"),
            "tar.gz" or "targz" or "tgz" => (ArchiveType.Tar, CompressionType.GZip, ".tar.gz"),
            _ => null,
        };

    private static string BuildArchiveName(CreateArchiveRequest request, string extension)
    {
        string requested = VirtualPath.SanitizeSegment(request.ArchiveName);

        if (requested.Length == 0)
        {
            // Name it after the single item being compressed, or the folder, as a file manager does.
            requested = request.Paths.Count == 1
                ? Path.GetFileNameWithoutExtension(VirtualPath.GetName(
                    VirtualPath.Normalize(request.Paths[0])))
                : "archive";
        }

        return requested.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            ? requested
            : requested + extension;
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
            logger.LogWarning(ex, "Could not remove partial archive {Physical}", physicalPath);
        }
    }
}