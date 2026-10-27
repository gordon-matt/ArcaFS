using System.Security.Cryptography;

namespace ArcaFS.Services.Search;

public sealed class SearchService(
    ILogger<SearchService> logger,
    IPermissionService permissionService,
    IRepository<Source> sourceRepository,
    IRepository<IndexedItem> indexRepository) : ISearchService
{
    /// <summary>
    /// Rows pulled from the index before access filtering. Filtering removes an unknown
    /// fraction, so we over-fetch and then page the filtered set. Bounded so a query matching
    /// half a source can't be turned into a memory exhaustion attack.
    /// </summary>
    private const int MaxCandidates = 5_000;

    public async Task<Result<SearchResultsDto>> SearchAsync(
        SearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = SearchQueryParser.Parse(request.Query);

        if (query.IsEmpty)
        {
            return Result<SearchResultsDto>.Success(new SearchResultsDto(
                request.Query ?? string.Empty, [], 0, 1, request.PageSize, null));
        }

        // Only sources the caller can browse are searched, so the index can never surface a
        // path they have no rule granting them.
        var accessible = await permissionService.GetAccessibleSourcesAsync(cancellationToken);

        var searchable = accessible
            .Where(s => s.RootPermissions.HasAny(FilePermission.Browse | FilePermission.Read))
            .Where(s => request.SourceId is null || s.Id == request.SourceId)
            .ToList();

        if (searchable.Count == 0)
        {
            return Result<SearchResultsDto>.Success(new SearchResultsDto(
                request.Query ?? string.Empty, [], 0, 1, request.PageSize, null));
        }

        var sourceIds = searchable.Select(s => s.Id).ToList();
        var sourceNames = searchable.ToDictionary(s => s.Id, s => s.Name);

        var predicate = BuildPredicate(query, sourceIds, request.Path);

        var rows = await indexRepository.FindAsync(new SearchOptions<IndexedItem>
        {
            Query = predicate,
            OrderBy = q => q.OrderByDescending(i => i.ModifiedAt),
            PageNumber = 1,
            PageSize = MaxCandidates,
            CancellationToken = cancellationToken,
        });

        var candidates = rows.ToList();

        // The index stores names case-insensitively for matching, so an exact-case search has
        // to be re-checked here rather than in SQL, where collation would decide for us.
        if (query.CaseSensitive && query.Terms.Count > 0)
        {
            candidates = candidates.Where(i => MatchesCaseSensitively(i, query)).ToList();
        }

        var visible = await FilterByAccessAsync(candidates, cancellationToken);

        int pageSize = Math.Clamp(request.PageSize, 1, 500);
        int pageNumber = Math.Max(1, request.PageNumber);

        var page = visible
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(i => new SearchResultItemDto(
                i.SourceId,
                sourceNames.GetValueOrDefault(i.SourceId, "?"),
                i.Path,
                i.Name,
                i.IsDirectory,
                i.SizeBytes,
                i.ModifiedAt,
                i.Kind))
            .ToList();

        string? warning = await BuildWarningAsync(searchable, candidates.Count, cancellationToken);

        return Result<SearchResultsDto>.Success(new SearchResultsDto(
            request.Query ?? string.Empty,
            page,
            visible.Count,
            pageNumber,
            pageSize,
            warning));
    }

    public async Task<Result<IReadOnlyList<DuplicateGroupDto>>> FindDuplicatesAsync(
        int sourceId,
        string path,
        long minimumSizeBytes = 1024,
        CancellationToken cancellationToken = default)
    {
        var resolved = await permissionService.RequireAsync(
            sourceId,
            path,
            FilePermission.Browse | FilePermission.Read,
            cancellationToken);

        if (!resolved.IsSuccess)
        {
            return resolved.To<IReadOnlyList<DuplicateGroupDto>>();
        }

        var target = resolved.Value;
        string prefix = target.AbsoluteVirtualPath == Constants.RootPath
            ? Constants.RootPath
            : target.AbsoluteVirtualPath + Constants.PathSeparator;

        var rows = await indexRepository.FindAsync(new SearchOptions<IndexedItem>
        {
            Query = i => i.SourceId == sourceId
                && !i.IsDirectory
                && i.SizeBytes >= minimumSizeBytes
                && (i.Path == target.AbsoluteVirtualPath || i.Path.StartsWith(prefix)),
            CancellationToken = cancellationToken,
        });

        // Two files of different lengths cannot be identical, so only same-size groups are
        // worth hashing. This is what makes the tool usable on a large source.
        var candidateGroups = rows
            .GroupBy(i => i.SizeBytes)
            .Where(g => g.Count() > 1)
            .ToList();

        if (candidateGroups.Count == 0)
        {
            return Result<IReadOnlyList<DuplicateGroupDto>>.Success([]);
        }

        var visible = await FilterByAccessAsync(
            candidateGroups.SelectMany(g => g).ToList(),
            cancellationToken);

        var groups = new List<DuplicateGroupDto>();
        string sourceName = target.Source.Name;

        foreach (var sizeGroup in visible.GroupBy(i => i.SizeBytes).Where(g => g.Count() > 1))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var hashed = new List<(IndexedItem Item, string Hash)>();

            foreach (var item in sizeGroup)
            {
                string? hash = await EnsureHashAsync(target.Source, item, cancellationToken);
                if (hash is not null)
                {
                    hashed.Add((item, hash));
                }
            }

            groups.AddRange(hashed
                .GroupBy(h => h.Hash, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => new DuplicateGroupDto(
                    g.Key,
                    sizeGroup.Key,
                    g.Select(h => new SearchResultItemDto(
                            h.Item.SourceId,
                            sourceName,
                            VirtualPath.MakeRelativeTo(h.Item.Path, target.Scope) ?? h.Item.Path,
                            h.Item.Name,
                            false,
                            h.Item.SizeBytes,
                            h.Item.ModifiedAt,
                            h.Item.Kind))
                        .ToList())));
        }

        IReadOnlyList<DuplicateGroupDto> ordered = groups
            .OrderByDescending(g => g.WastedBytes)
            .ToList();

        return Result<IReadOnlyList<DuplicateGroupDto>>.Success(ordered);
    }

    public async Task<Result<DiskUsageDto>> GetDiskUsageAsync(
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
            return resolved.To<DiskUsageDto>();
        }

        var target = resolved.Value;

        // ParentPath is denormalised in the index precisely so this is an equality match rather
        // than a prefix scan over the whole source.
        var rows = await indexRepository.FindAsync(new SearchOptions<IndexedItem>
        {
            Query = i => i.SourceId == sourceId && i.ParentPath == target.AbsoluteVirtualPath,
            CancellationToken = cancellationToken,
        });

        var visible = await FilterByAccessAsync(rows.ToList(), cancellationToken);

        long total = visible.Sum(i => i.SizeBytes);

        IReadOnlyList<DiskUsageEntryDto> entries = visible
            .OrderByDescending(i => i.SizeBytes)
            .Select(i => new DiskUsageEntryDto(
                i.Name,
                VirtualPath.MakeRelativeTo(i.Path, target.Scope) ?? i.Path,
                i.IsDirectory,
                i.SizeBytes,
                total == 0 ? 0 : Math.Round(i.SizeBytes * 100d / total, 2)))
            .ToList();

        return Result<DiskUsageDto>.Success(new DiskUsageDto(
            sourceId,
            target.ScopedVirtualPath,
            total,
            entries));
    }

    private static Expression<Func<IndexedItem, bool>> BuildPredicate(
        SearchQuery query,
        List<int> sourceIds,
        string? scopePath)
    {
        var predicate = PredicateBuilder.New<IndexedItem>(i => sourceIds.Contains(i.SourceId));

        if (!string.IsNullOrWhiteSpace(scopePath) && scopePath != Constants.RootPath)
        {
            string normalized = VirtualPath.Normalize(scopePath);
            string prefix = normalized + Constants.PathSeparator;

            predicate = predicate.And(i => i.Path.StartsWith(prefix));
        }

        if (query.Terms.Count > 0)
        {
            // Matching happens against NameLower, which the indexer populates, so this behaves
            // the same on a case-sensitive Postgres collation and a case-insensitive MySQL one.
            var termPredicate = query.RequireAllTerms
                ? PredicateBuilder.New<IndexedItem>(true)
                : PredicateBuilder.New<IndexedItem>(false);

            foreach (string term in query.Terms)
            {
                string lowered = term.ToLowerInvariant();

                termPredicate = query.RequireAllTerms
                    ? termPredicate.And(i => i.NameLower.Contains(lowered))
                    : termPredicate.Or(i => i.NameLower.Contains(lowered));
            }

            predicate = predicate.And(termPredicate);
        }

        if (query.Kinds.Count > 0)
        {
            var kinds = query.Kinds.ToList();
            predicate = predicate.And(i => kinds.Contains(i.Kind));
        }

        if (query.FoldersOnly is bool foldersOnly)
        {
            predicate = predicate.And(i => i.IsDirectory == foldersOnly);
        }

        if (query.LargerThanBytes is long larger)
        {
            predicate = predicate.And(i => i.SizeBytes > larger);
        }

        if (query.SmallerThanBytes is long smaller)
        {
            predicate = predicate.And(i => i.SizeBytes < smaller);
        }

        if (query.NewerThanUtc is DateTime newer)
        {
            predicate = predicate.And(i => i.ModifiedAt > newer);
        }

        if (query.OlderThanUtc is DateTime older)
        {
            predicate = predicate.And(i => i.ModifiedAt < older);
        }

        return predicate;
    }

    private static bool MatchesCaseSensitively(IndexedItem item, SearchQuery query) =>
        query.RequireAllTerms
            ? query.Terms.All(t => item.Name.Contains(t, StringComparison.Ordinal))
            : query.Terms.Any(t => item.Name.Contains(t, StringComparison.Ordinal));

    /// <summary>
    /// Drops rows the caller has no Read permission on. Evaluators are built once per source and
    /// per parent folder, so a page of results costs a handful of rule evaluations rather than one
    /// per row.
    /// </summary>
    private async Task<List<IndexedItem>> FilterByAccessAsync(
        List<IndexedItem> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return items;
        }

        var principal = await permissionService.GetCurrentPrincipalAsync(cancellationToken);
        var evaluators = new Dictionary<(int SourceId, string ParentPath), Func<string, FilePermission>>();
        var visible = new List<IndexedItem>(items.Count);

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = (item.SourceId, item.ParentPath);

            if (!evaluators.TryGetValue(key, out var evaluate))
            {
                evaluate = await permissionService.GetChildPermissionEvaluatorAsync(
                    item.SourceId,
                    item.ParentPath,
                    principal,
                    cancellationToken);

                evaluators[key] = evaluate;
            }

            if (evaluate(item.Path).HasAny(FilePermission.Browse | FilePermission.Read))
            {
                visible.Add(item);
            }
        }

        return visible;
    }

    /// <summary>
    /// Returns the file's content hash, computing and persisting it on first use. Hashes are
    /// cleared by the indexer whenever a file's size or timestamp changes, so a cached value is
    /// always for the current bytes.
    /// </summary>
    private async Task<string?> EnsureHashAsync(
        Source source,
        IndexedItem item,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(item.ContentHash))
        {
            return item.ContentHash;
        }

        if (!VirtualPath.TryResolve(source.Path, item.Path, out string? physicalPath)
            || !File.Exists(physicalPath))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(
                physicalPath,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.Read,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                });

            byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
            string hex = Convert.ToHexStringLower(hash);

            item.ContentHash = hex;
            await indexRepository.UpdateAsync(item);

            return hex;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Could not hash {Physical} for duplicate detection", physicalPath);
            return null;
        }
    }

    /// <summary>
    /// Warns when results may be incomplete, which is otherwise very confusing: a user who
    /// knows a file exists and can't find it should be told the index is still building.
    /// </summary>
    private async Task<string?> BuildWarningAsync(
        List<SourceSummaryDto> searchable,
        int candidateCount,
        CancellationToken cancellationToken)
    {
        if (candidateCount >= MaxCandidates)
        {
            return $"Only the first {MaxCandidates:N0} matches were considered. Narrow the search to see more.";
        }

        var ids = searchable.Select(s => s.Id).ToList();

        var unindexed = await sourceRepository.FindAsync(new SearchOptions<Source>
        {
            Query = s => ids.Contains(s.Id) && s.IsIndexed && s.LastIndexedAt == null,
            CancellationToken = cancellationToken,
        });

        var names = unindexed.Select(s => s.Name).ToList();

        return names.Count == 0
            ? null
            : names.Count == 1
            ? $"'{names[0]}' has not been indexed yet, so its contents are not searchable."
            : $"{names.Count} sources have not been indexed yet, so their contents are not searchable.";
    }
}