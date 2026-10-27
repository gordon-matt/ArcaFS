namespace ArcaFS.Services.Dashboard;

public sealed class DashboardService(
    IUserContextService userContext,
    IPermissionService permissionService,
    IActivityService activityService,
    IUserInfoService userInfoService,
    IRepository<Share> shareRepository,
    IRepository<AccessRule> ruleRepository) : IDashboardService
{
    public async Task<Result<DashboardStatsDto>> GetStatsAsync(
        CancellationToken cancellationToken = default)
    {
        // The source list is already access-filtered, and the totals are derived from it, so a
        // standard user's dashboard describes their world rather than the server's.
        var sources = await permissionService.GetAccessibleSourcesAsync(cancellationToken);

        bool isAdministrator = userContext.IsAdministrator();
        string? userId = userContext.GetCurrentUserId();

        int userCount = 0;
        int ruleCount = 0;

        if (isAdministrator)
        {
            var users = await userInfoService.GetAllUsersAsync(cancellationToken);
            userCount = users.Count;
            ruleCount = await ruleRepository.CountAsync(r => true);
        }

        var now = DateTime.UtcNow;

        int activeShares = await shareRepository.CountAsync(s =>
            !s.IsDisabled
            && (s.ExpiresAt == null || s.ExpiresAt > now)
            && (isAdministrator || s.OwnerUserId == userId));

        var recent = await activityService.QueryAsync(
            new ActivityQueryRequest { PageNumber = 1, PageSize = 10 },
            cancellationToken);

        return Result<DashboardStatsDto>.Success(new DashboardStatsDto(
            sources.Count,
            sources.Sum(s => s.IndexedItemCount ?? 0),
            sources.Sum(s => s.IndexedSizeBytes ?? 0),
            userCount,
            activeShares,
            ruleCount,
            sources.Max(s => s.LastIndexedAt),
            sources,
            recent.Items));
    }
}