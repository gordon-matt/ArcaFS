namespace ArcaFS.Services.Dashboard;

/// <summary>Aggregates the home page tiles. Every figure respects the caller's access.</summary>
public interface IDashboardService
{
    Task<Result<DashboardStatsDto>> GetStatsAsync(CancellationToken cancellationToken = default);
}