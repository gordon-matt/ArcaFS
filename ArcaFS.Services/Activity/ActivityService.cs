namespace ArcaFS.Services.Activity;

/// <inheritdoc cref="IActivityService" />
public sealed class ActivityService(
    ILogger<ActivityService> logger,
    IUserContextService userContext,
    IUserInfoService userInfoService,
    IRepository<ActivityEvent> repository,
    IRepository<Source> sourceRepository,
    IHttpContextAccessor httpContextAccessor) : IActivityService
{
    /// <inheritdoc />
    public async Task RecordAsync(
        ActivityKind kind,
        int? sourceId,
        string? path,
        bool succeeded = true,
        string? targetPath = null,
        long? sizeBytes = null,
        string? detail = null,
        string? shareToken = null,
        string? overrideUserId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await repository.InsertAsync(new ActivityEvent
            {
                Kind = kind,
                UserId = overrideUserId ?? userContext.GetCurrentUserId(),
                SourceId = sourceId,
                Path = Truncate(path, Constants.MaxLengths.Path),
                TargetPath = Truncate(targetPath, Constants.MaxLengths.Path),
                ShareToken = Truncate(shareToken, Constants.MaxLengths.ShareToken),
                SizeBytes = sizeBytes,
                Succeeded = succeeded,
                Detail = Truncate(detail, 2048),
                IpAddress = ResolveIpAddress(),
                OccurredAt = DateTime.UtcNow,
            });
        }
        catch (Exception ex)
        {
            // An audit write must never take a user's operation down with it. The operation
            // itself already succeeded by the time we get here.
            logger.LogError(ex, "Failed to record {Kind} activity for path {Path}", kind, path);
        }
    }

    /// <inheritdoc />
    public async Task<PagedList<ActivityEventDto>> QueryAsync(
        ActivityQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        bool isAdministrator = userContext.IsAdministrator();
        string? currentUserId = userContext.GetCurrentUserId();

        // Non-administrators only ever see their own history, whatever they ask for.
        string? userFilter = isAdministrator ? request.UserId : currentUserId;

        int pageSize = Math.Clamp(request.PageSize, 1, 500);
        int pageNumber = Math.Max(1, request.PageNumber);

        var predicate = BuildPredicate(request, userFilter);

        int total = await repository.CountAsync(predicate);

        var events = await repository.FindAsync(new SearchOptions<ActivityEvent>
        {
            Query = predicate,
            OrderBy = q => q.OrderByDescending(e => e.OccurredAt),
            PageNumber = pageNumber,
            PageSize = pageSize,
            CancellationToken = cancellationToken,
        });

        var list = events.ToList();

        var users = await userInfoService.GetUserInfoAsync(
            list.Where(e => e.UserId is not null).Select(e => e.UserId!).Distinct(StringComparer.Ordinal),
            cancellationToken);

        var sourceNames = await GetSourceNamesAsync(list, cancellationToken);

        IReadOnlyList<ActivityEventDto> items = list
            .Select(e => new ActivityEventDto(
                e.Id,
                e.Kind,
                e.UserId,
                e.UserId is null ? null : users.GetValueOrDefault(e.UserId)?.Label,
                e.SourceId,
                e.SourceId is null ? null : sourceNames.GetValueOrDefault(e.SourceId.Value),
                e.Path,
                e.TargetPath,
                e.ShareToken,
                e.SizeBytes,
                e.Succeeded,
                e.Detail,
                e.IpAddress,
                e.OccurredAt))
            .ToList();

        return new PagedList<ActivityEventDto>(items, total, pageNumber, pageSize);
    }

    /// <inheritdoc />
    public async Task<int> PurgeOlderThanAsync(int retentionDays, CancellationToken cancellationToken = default)
    {
        if (retentionDays <= 0)
        {
            return 0;
        }

        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        var stale = await repository.FindAsync(new SearchOptions<ActivityEvent>
        {
            Query = e => e.OccurredAt < cutoff,
            CancellationToken = cancellationToken,
        });

        var list = stale.ToList();
        if (list.Count == 0)
        {
            return 0;
        }

        await repository.DeleteAsync(list);
        logger.LogInformation("Purged {Count} activity event(s) older than {Cutoff:u}", list.Count, cutoff);

        return list.Count;
    }

    private static Expression<Func<ActivityEvent, bool>> BuildPredicate(
        ActivityQueryRequest request,
        string? userFilter)
    {
        // Composed as a single expression tree so the whole filter runs in the database rather
        // than paging the table into memory.
        var predicate = PredicateBuilder.New<ActivityEvent>(true);

        if (!string.IsNullOrEmpty(userFilter))
        {
            predicate = predicate.And(e => e.UserId == userFilter);
        }

        if (request.SourceId is int sourceId)
        {
            predicate = predicate.And(e => e.SourceId == sourceId);
        }

        if (request.Kind is ActivityKind kind)
        {
            predicate = predicate.And(e => e.Kind == kind);
        }

        if (request.FromUtc is DateTime from)
        {
            predicate = predicate.And(e => e.OccurredAt >= from);
        }

        if (request.ToUtc is DateTime to)
        {
            predicate = predicate.And(e => e.OccurredAt <= to);
        }

        if (request.SucceededOnly is bool succeeded)
        {
            predicate = predicate.And(e => e.Succeeded == succeeded);
        }

        if (!string.IsNullOrWhiteSpace(request.PathContains))
        {
            string term = request.PathContains.Trim();
            predicate = predicate.And(e => e.Path != null && e.Path.Contains(term));
        }

        return predicate;
    }

    private async Task<Dictionary<int, string>> GetSourceNamesAsync(
        List<ActivityEvent> events,
        CancellationToken cancellationToken)
    {
        var ids = events.Where(e => e.SourceId is not null).Select(e => e.SourceId!.Value).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var sources = await sourceRepository.FindAsync(new SearchOptions<Source>
        {
            Query = s => ids.Contains(s.Id),
            CancellationToken = cancellationToken,
        });

        return sources.ToDictionary(s => s.Id, s => s.Name);
    }

    private string? ResolveIpAddress()
    {
        var context = httpContextAccessor.HttpContext;
        if (context is null)
        {
            return null;
        }

        // Behind a reverse proxy the socket address is the proxy's. ForwardedHeaders middleware
        // rewrites RemoteIpAddress when the proxy is trusted, so reading it here is correct
        // either way.
        return context.Connection.RemoteIpAddress?.ToString();
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];
}