using Hangfire.Dashboard;

namespace ArcaFS.Infrastructure;

/// <summary>
/// Restricts the Hangfire dashboard to administrators.
/// </summary>
/// <remarks>
/// Hangfire's dashboard is unauthenticated by default and lets a visitor enqueue and delete
/// jobs, so an explicit filter is required rather than optional.
/// </remarks>
public sealed class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        return httpContext.User.Identity?.IsAuthenticated == true
            && httpContext.User.IsInRole(Constants.Roles.Administrator);
    }
}
