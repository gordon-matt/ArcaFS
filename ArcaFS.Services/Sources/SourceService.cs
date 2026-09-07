namespace ArcaFS.Services.Sources;

public sealed class SourceService(
    ILogger<SourceService> logger,
    IUserContextService userContext,
    IPermissionService permissionService,
    IRepository<Source> repository,
    IRepository<AccessRule> ruleRepository,
    IRepository<IndexedItem> indexRepository) : ISourceService
{
    public async Task<Result<IReadOnlyList<SourceDto>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result<IReadOnlyList<SourceDto>>.Forbidden();
        }

        var sources = await repository.FindAsync(new SearchOptions<Source>
        {
            OrderBy = q => q.OrderBy(s => s.SortOrder).ThenBy(s => s.Name),
            CancellationToken = cancellationToken,
        });

        var list = sources.ToList();
        var ruleCounts = await GetRuleCountsAsync(cancellationToken);

        IReadOnlyList<SourceDto> dtos = list
            .Select(s => ToDto(s, ruleCounts.GetValueOrDefault(s.Id)))
            .ToList();

        return Result<IReadOnlyList<SourceDto>>.Success(dtos);
    }

    public async Task<Result<SourceDto>> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result<SourceDto>.Forbidden();
        }

        var source = await repository.FindOneAsync(new SearchOptions<Source>
        {
            Query = s => s.Id == id,
            CancellationToken = cancellationToken,
        });

        if (source is null)
        {
            return Result<SourceDto>.NotFound($"Source {id} was not found.");
        }

        int ruleCount = await ruleRepository.CountAsync(r => r.SourceId == id);

        return Result<SourceDto>.Success(ToDto(source, ruleCount));
    }

    public async Task<Result<SourceDto>> CreateAsync(
        CreateSourceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result<SourceDto>.Forbidden();
        }

        var validation = await ValidateAsync(request, existingId: null, cancellationToken);
        if (!validation.IsSuccess)
        {
            return validation.To<SourceDto>();
        }

        var source = new Source
        {
            Name = request.Name.Trim(),
            Path = NormalizeHostPath(request.Path),
        };

        Apply(source, request);

        var created = await repository.InsertAsync(source);

        logger.LogInformation(
            "Source {SourceId} '{Name}' created at {Path} by {User}",
            created.Id,
            created.Name,
            created.Path,
            userContext.GetCurrentUserName());

        return Result<SourceDto>.Success(ToDto(created, 0));
    }

    public async Task<Result<SourceDto>> UpdateAsync(
        UpdateSourceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result<SourceDto>.Forbidden();
        }

        var source = await repository.FindOneAsync(new SearchOptions<Source>
        {
            Query = s => s.Id == request.Id,
            CancellationToken = cancellationToken,
        });

        if (source is null)
        {
            return Result<SourceDto>.NotFound($"Source {request.Id} was not found.");
        }

        var validation = await ValidateAsync(request, source.Id, cancellationToken);
        if (!validation.IsSuccess)
        {
            return validation.To<SourceDto>();
        }

        bool pathChanged = !string.Equals(
            source.Path,
            NormalizeHostPath(request.Path),
            StringComparison.Ordinal);

        source.Name = request.Name.Trim();
        source.Path = NormalizeHostPath(request.Path);
        source.IsDisabled = request.IsDisabled;
        source.SortOrder = request.SortOrder;

        Apply(source, request);

        await repository.UpdateAsync(source);

        // Rules and scopes are interpreted relative to the root, and cached per source, so any
        // change to how paths resolve has to drop the cache.
        permissionService.InvalidateSource(source.Id);

        if (pathChanged)
        {
            // The old index describes a directory tree that is no longer this source.
            await ClearIndexAsync(source.Id, cancellationToken);
            logger.LogInformation(
                "Source {SourceId} root changed to {Path}; its index was cleared",
                source.Id,
                source.Path);
        }

        int ruleCount = await ruleRepository.CountAsync(r => r.SourceId == source.Id);

        return Result<SourceDto>.Success(ToDto(source, ruleCount));
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var source = await repository.FindOneAsync(new SearchOptions<Source>
        {
            Query = s => s.Id == id,
            CancellationToken = cancellationToken,
        });

        if (source is null)
        {
            return Result.NotFound($"Source {id} was not found.");
        }

        var rules = await ruleRepository.FindAsync(new SearchOptions<AccessRule>
        {
            Query = r => r.SourceId == id,
            CancellationToken = cancellationToken,
        });

        var ruleList = rules.ToList();
        if (ruleList.Count > 0)
        {
            await ruleRepository.DeleteAsync(ruleList);
        }

        await ClearIndexAsync(id, cancellationToken);
        await repository.DeleteAsync(source);

        permissionService.InvalidateSource(id);

        // Worth being explicit in the log: operators reasonably worry that removing a source
        // removed their files.
        logger.LogInformation(
            "Source {SourceId} '{Name}' removed from ArcaFS by {User}. The directory {Path} was left untouched.",
            id,
            source.Name,
            userContext.GetCurrentUserName(),
            source.Path);

        return Result.Success();
    }

    public async Task<Result<SourcePathProbeDto>> ProbePathAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result<SourcePathProbeDto>.Forbidden();
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return Result<SourcePathProbeDto>.Invalid(new ValidationError(
                nameof(path), "Enter a path."));
        }

        string normalized;
        try
        {
            normalized = NormalizeHostPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Result<SourcePathProbeDto>.Success(new SourcePathProbeDto(
                path, false, false, false, false, null, "That is not a valid path."));
        }

        if (File.Exists(normalized))
        {
            return Result<SourcePathProbeDto>.Success(new SourcePathProbeDto(
                normalized, true, false, false, false, null, "That is a file. Choose a folder."));
        }

        if (!Directory.Exists(normalized))
        {
            return Result<SourcePathProbeDto>.Success(new SourcePathProbeDto(
                normalized, false, false, false, false, null, "That folder does not exist."));
        }

        bool canRead = false;
        int? childCount = null;
        string? message = null;

        try
        {
            // Enumerating is the only reliable way to know the process can read it; checking
            // ACLs by hand gets Unix permissions and Windows inheritance wrong in different ways.
            childCount = Directory.EnumerateFileSystemEntries(normalized).Take(1000).Count();
            canRead = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            message = "The server cannot read that folder. Check its filesystem permissions.";
            logger.LogWarning(ex, "Probe could not enumerate {Path}", normalized);
        }

        bool canWrite = canRead && CanWriteTo(normalized);

        if (canRead && !canWrite)
        {
            message = "The server can read but not write to that folder, so uploads will fail.";
        }

        return Result<SourcePathProbeDto>.Success(new SourcePathProbeDto(
            normalized, true, true, canRead, canWrite, childCount, message));
    }

    public async Task<Result<IReadOnlyList<SourceIndexStatusDto>>> GetIndexStatusAsync(
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result<IReadOnlyList<SourceIndexStatusDto>>.Forbidden();
        }

        var sources = await repository.FindAsync(new SearchOptions<Source>
        {
            OrderBy = q => q.OrderBy(s => s.SortOrder).ThenBy(s => s.Name),
            CancellationToken = cancellationToken,
        });

        IReadOnlyList<SourceIndexStatusDto> statuses = sources
            .Select(s => new SourceIndexStatusDto(
                s.Id,
                IndexScanState.IsScanning(s.Id),
                s.LastIndexedAt,
                s.IndexedItemCount,
                s.IndexedSizeBytes))
            .ToList();

        return Result<IReadOnlyList<SourceIndexStatusDto>>.Success(statuses);
    }

    private async Task<Result> ValidateAsync(
        CreateSourceRequest request,
        int? existingId,
        CancellationToken cancellationToken)
    {
        string name = request.Name?.Trim() ?? string.Empty;

        if (name.Length == 0)
        {
            return Result.Invalid(new ValidationError(nameof(request.Name), "Enter a name."));
        }

        if (name.Length > Constants.MaxLengths.SourceName)
        {
            return Result.Invalid(new ValidationError(
                nameof(request.Name),
                $"Names are limited to {Constants.MaxLengths.SourceName} characters."));
        }

        if (string.IsNullOrWhiteSpace(request.Path))
        {
            return Result.Invalid(new ValidationError(nameof(request.Path), "Enter a path."));
        }

        // The name is a URL segment, so a slash in it would silently break every link into
        // the source.
        if (name.Contains(Constants.PathSeparator) || name.Contains('\\'))
        {
            return Result.Invalid(new ValidationError(
                nameof(request.Name), "Names cannot contain slashes."));
        }

        var clash = await repository.FindOneAsync(new SearchOptions<Source>
        {
            Query = existingId is int id
                ? s => s.Id != id && s.Name == name
                : s => s.Name == name,
            CancellationToken = cancellationToken,
        });

        if (clash is not null)
        {
            return Result.Conflict($"A source named '{name}' already exists.");
        }

        if (request.IndexIntervalMinutes < 0)
        {
            return Result.Invalid(new ValidationError(
                nameof(request.IndexIntervalMinutes), "The interval cannot be negative."));
        }

        if (request.CreateUserDirectory && string.IsNullOrWhiteSpace(request.DefaultUserScope))
        {
            return Result.Invalid(new ValidationError(
                nameof(request.CreateUserDirectory),
                "Creating per-user folders needs a default user scope, such as '/${username}'."));
        }

        return Result.Success();
    }

    private static void Apply(Source source, CreateSourceRequest request)
    {
        source.Description = request.Description?.Trim();
        source.IsReadOnly = request.IsReadOnly;
        source.DenyByDefault = request.DenyByDefault;
        source.DefaultPermissions = request.DefaultPermissions;
        source.AdministratorsBypassRules = request.AdministratorsBypassRules;
        source.DefaultUserScope = string.IsNullOrWhiteSpace(request.DefaultUserScope)
            ? null
            : VirtualPath.Normalize(request.DefaultUserScope);
        source.CreateUserDirectory = request.CreateUserDirectory;
        source.IsVisibleInSidebar = request.IsVisibleInSidebar;
        source.IgnoreHiddenFiles = request.IgnoreHiddenFiles;
        source.FollowSymlinks = request.FollowSymlinks;
        source.IsIndexed = request.IsIndexed;
        source.IndexIntervalMinutes = request.IndexIntervalMinutes;
        source.ExcludePatterns = string.IsNullOrWhiteSpace(request.ExcludePatterns)
            ? null
            : request.ExcludePatterns.Trim();
    }

    private static SourceDto ToDto(Source source, int accessRuleCount) => new(
        source.Id,
        source.Name,
        source.Path,
        source.Description,
        source.IsDisabled,
        source.IsReadOnly,
        source.DenyByDefault,
        source.DefaultPermissions,
        source.AdministratorsBypassRules,
        source.DefaultUserScope,
        source.CreateUserDirectory,
        source.IsVisibleInSidebar,
        source.IgnoreHiddenFiles,
        source.FollowSymlinks,
        source.IsIndexed,
        source.IndexIntervalMinutes,
        source.ExcludePatterns,
        source.LastIndexedAt,
        source.IndexedItemCount,
        source.IndexedSizeBytes,
        source.SortOrder,
        accessRuleCount,
        Directory.Exists(source.Path));

    private async Task<Dictionary<int, int>> GetRuleCountsAsync(CancellationToken cancellationToken)
    {
        var rules = await ruleRepository.FindAsync(new SearchOptions<AccessRule>
        {
            CancellationToken = cancellationToken,
        });

        return rules
            .GroupBy(r => r.SourceId)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    private async Task ClearIndexAsync(int sourceId, CancellationToken cancellationToken)
    {
        var rows = await indexRepository.FindAsync(new SearchOptions<IndexedItem>
        {
            Query = i => i.SourceId == sourceId,
            CancellationToken = cancellationToken,
        });

        var list = rows.ToList();
        if (list.Count > 0)
        {
            await indexRepository.DeleteAsync(list);
        }
    }

    /// <summary>
    /// Trims a trailing separator and resolves the path to its absolute form, so
    /// <c>/data/media/</c> and <c>/data/media</c> are stored identically and comparisons hold.
    /// </summary>
    private static string NormalizeHostPath(string path)
    {
        string trimmed = path.Trim();
        string full = Path.GetFullPath(trimmed);

        return full.Length > 1 && (full.EndsWith(Path.DirectorySeparatorChar)
            || full.EndsWith(Path.AltDirectorySeparatorChar))
            ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : full;
    }

    /// <summary>
    /// Probes write access by creating and removing a uniquely-named temp file. Inspecting ACLs
    /// instead would have to model Unix modes, Windows inheritance and container user mapping,
    /// and would still be a guess.
    /// </summary>
    private bool CanWriteTo(string directory)
    {
        string probe = Path.Combine(directory, $".arcafs-write-probe-{Guid.NewGuid():N}");

        try
        {
            using (File.Create(probe))
            {
            }

            File.Delete(probe);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Write probe failed for {Path}", directory);
            return false;
        }
    }
}
