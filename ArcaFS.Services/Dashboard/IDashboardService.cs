namespace ArcaFS.Services.Dashboard;

/// <summary>Aggregates the home page tiles. Every figure respects the caller's access.</summary>
public interface IDashboardService
{
    /// <summary>
    /// Home-page figures. Source counts come from the sources the caller can see. User and rule
    /// counts are filled in only for an administrator. Recent activity is the caller's own
    /// events unless they are an administrator.
    /// </summary>
    /// <param name="cancellationToken">Cancels the lookups.</param>
    /// <returns>The figures, including the accessible sources and the ten most recent events.</returns>
    Task<Result<DashboardStatsDto>> GetStatsAsync(CancellationToken cancellationToken = default);
}
