namespace ArcaFS.Infrastructure;

/// <summary>
/// Sends every request to <c>/setup</c> until the first-run wizard has been completed.
/// </summary>
/// <remarks>
/// Placed after authentication so the wizard can tell the visitor who they are signed in as, and
/// before endpoint routing so it can short-circuit Blazor and controller routes alike. The
/// exemption list is deliberately narrow: anything not needed to render and complete the wizard
/// stays unreachable, which keeps an unconfigured instance from exposing half-initialised state.
/// </remarks>
public sealed class SetupRedirectMiddleware(RequestDelegate next)
{
    private const string SetupPath = "/setup";

    /// <summary>
    /// Prefixes that must keep working during setup: the wizard itself, the framework plumbing
    /// Blazor needs to render it, static assets, sign-in pages, and the health endpoint.
    /// </summary>
    private static readonly string[] ExemptPrefixes =
    [
        SetupPath,
        "/_blazor",
        "/_framework",
        "/_content",
        "/css",
        "/js",
        "/lib",
        "/img",
        "/favicon",
        "/health",
        "/error",
        "/access-denied",
        "/auth/",
        "/Identity/",
        "/logvue",
    ];

    public async Task InvokeAsync(HttpContext context, IServerSettingsService settingsService)
    {
        if (IsExempt(context.Request.Path))
        {
            await next(context);
            return;
        }

        if (await settingsService.IsSetupCompleteAsync(context.RequestAborted))
        {
            await next(context);
            return;
        }

        // A redirect would turn an API call into a confusing 200 with HTML in it, so
        // non-navigation requests get a status code instead.
        if (!IsNavigation(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        context.Response.Redirect(SetupPath);
    }

    private static bool IsExempt(PathString path) =>
        ExemptPrefixes.Any(prefix =>
            path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase)
            || path.Value?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true);

    private static bool IsNavigation(HttpRequest request) =>
        HttpMethods.IsGet(request.Method)
        && request.Headers.Accept.Any(value =>
            value?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true);
}
