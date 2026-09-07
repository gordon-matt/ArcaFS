using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ArcaFS.Infrastructure;

/// <summary>
/// Authenticates every request as a single synthetic administrator.
/// </summary>
/// <remarks>
/// This is what makes the desktop build usable: someone who installs ArcaFS to browse their own
/// files should not have to invent a password first. It is also why the desktop packaging forces
/// this mode together with SQLite, and why the mode must never be selected for a server that is
/// reachable by anyone else — there is no authentication here at all, by design.
/// </remarks>
public sealed class NoneAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "None";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, Constants.DefaultUserId),
                new Claim(ClaimTypes.Name, Constants.DefaultUserName),
                new Claim(ClaimTypes.Role, Constants.Roles.Administrator),
                new Claim(ClaimTypes.Role, Constants.Roles.User),
            ],
            authenticationType: SchemeName,
            nameType: ClaimTypes.Name,
            roleType: ClaimTypes.Role);

        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
