using System.Security.Claims;
using ArcaFS.Services.Auth;

namespace ArcaFS.Tests.Auth;

public class ClaimsPrincipalExtensionsTests
{
    [Fact]
    public void FindUserId_PrefersNameIdentifier()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "mapped-id"),
            new Claim("sub", "keycloak-sub"),
        ], "test"));

        Assert.Equal("mapped-id", user.FindUserId());
    }

    [Fact]
    public void FindUserId_FallsBackToSub()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", "keycloak-sub"),
            new Claim("preferred_username", "matt"),
        ], "test"));

        Assert.Equal("keycloak-sub", user.FindUserId());
    }
}
