using ArcaFS.Data.MySql;
using Microsoft.AspNetCore.Authentication;
using ArcaFS.Data.Npgsql;
using ArcaFS.Data.Sql;
using ArcaFS.Data.Sqlite;
using Hangfire;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace ArcaFS.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Reads <c>Database:Provider</c> and delegates to the matching provider project. Failing
    /// loudly on an unknown value beats silently starting on SQLite when the operator meant
    /// Postgres and mistyped it.
    /// </summary>
    public static IServiceCollection AddArcaFsDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string provider = ArcaFsConfiguration.ResolveDatabaseProvider(configuration);

        return provider switch
        {
            Constants.DatabaseProviders.Sqlite => services.AddArcaFSSqlite(configuration),
            Constants.DatabaseProviders.SqlServer => services.AddArcaFSSqlServer(configuration),
            Constants.DatabaseProviders.Npgsql => services.AddArcaFSNpgsql(configuration),
            Constants.DatabaseProviders.MySql => services.AddArcaFSMySql(configuration),
            _ => throw new InvalidOperationException(
                $"Unknown Database:Provider '{provider}'. Valid values: Sqlite, SqlServer, Npgsql, MySql."),
        };
    }

    public static IServiceCollection AddArcaFsRepositories(this IServiceCollection services)
    {
        // Extenso maps the open generic IRepository<> to its EF implementation; no scanning.
        services.AddEntityFrameworkRepository();

        return services;
    }

    public static IServiceCollection AddArcaFsHangfire(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string provider = ArcaFsConfiguration.ResolveDatabaseProvider(configuration);

        // Hangfire gets its own connection string when one is supplied, so an operator can keep
        // job state off the application database.
        string connectionString = configuration.GetConnectionString("Hangfire")
            is { Length: > 0 } dedicated
            ? dedicated
            : configuration.GetConnectionString("DefaultConnection") ?? string.Empty;

        switch (provider)
        {
            case Constants.DatabaseProviders.Sqlite:
                services.AddArcaFSSqliteHangfire(configuration["Hangfire:SqlitePath"]);
                break;

            case Constants.DatabaseProviders.SqlServer:
                services.AddArcaFSSqlServerHangfire(connectionString);
                break;

            case Constants.DatabaseProviders.Npgsql:
                services.AddArcaFSNpgsqlHangfire(connectionString);
                break;

            case Constants.DatabaseProviders.MySql:
                services.AddArcaFSMySqlHangfire(connectionString);
                break;

            default:
                throw new InvalidOperationException($"Unknown Database:Provider '{provider}'.");
        }

        // Separate servers per queue so a long index scan can't starve interactive work.
        services.AddHangfireServer(options =>
        {
            options.ServerName = "main";
            options.Queues =
            [
                Constants.HangfireQueues.Default,
                Constants.HangfireQueues.Critical,
            ];
        });

        // One worker for indexing: two concurrent scans of the same disk are slower than one,
        // and IndexScanState already refuses to scan a source twice at once.
        services.AddHangfireServer(options =>
        {
            options.ServerName = "index";
            options.Queues = [Constants.HangfireQueues.Index];
            options.WorkerCount = 1;
        });

        services.AddHangfireServer(options =>
        {
            options.ServerName = "thumbnails";
            options.Queues = [Constants.HangfireQueues.Thumbnails];
            options.WorkerCount = Math.Max(1, Environment.ProcessorCount / 2);
        });

        return services;
    }

    /// <summary>
    /// Wires up whichever authentication provider is configured and returns which one it was,
    /// so <c>Program.cs</c> can decide whether to map Identity's Razor Pages and what the seeder should do.
    /// </summary>
    public static AuthProvider AddArcaFsAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string provider = configuration["Authentication:Provider"] is { Length: > 0 } configured
            ? configured
            : Constants.AuthProviders.Identity;

        // The default context service resolves the user from HttpContext or, inside a Blazor
        // circuit where there is no HttpContext, from the AuthenticationStateProvider.
        services.AddScoped<IUserContextService, BlazorAwareUserContextService>();

        switch (provider)
        {
            case Constants.AuthProviders.Identity:
                return services.AddIdentityAuthentication();

            case Constants.AuthProviders.Keycloak:
                return services.AddKeycloakAuthentication(configuration);

            case Constants.AuthProviders.None:
                return services.AddNoAuthentication();

            default:
                throw new InvalidOperationException(
                    $"Unknown Authentication:Provider '{provider}'. Valid values: Identity, Keycloak, None.");
        }
    }

    private static AuthProvider AddIdentityAuthentication(this IServiceCollection services)
    {
        services.AddSingleton<IAuthProviderService>(new AuthProviderService(AuthProvider.Identity, null));

        services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
            {
                // Relaxed from the defaults deliberately. This is a file browser people run for
                // themselves and small teams; a six-character minimum with no character-class
                // rules is the trade-off Shelfwarden makes and it applies equally here.
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredLength = 6;

                options.User.RequireUniqueEmail = false;
                options.SignIn.RequireConfirmedAccount = false;
            })
            .AddEntityFrameworkStores<ApplicationDbContextBase>()
            .AddDefaultTokenProviders()
            .AddDefaultUI();

        services.AddScoped<IUserInfoService, AspNetIdentityUserInfoService>();
        services.AddScoped<IAdminUserManagementService, AdminIdentityUserManagementService>();

        return AuthProvider.Identity;
    }

    private static AuthProvider AddNoAuthentication(this IServiceCollection services)
    {
        services.AddSingleton<IAuthProviderService>(new AuthProviderService(AuthProvider.None, null));

        services.AddAuthentication(NoneAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, NoneAuthenticationHandler>(
                NoneAuthenticationHandler.SchemeName,
                _ => { });

        // Replace the HttpContext-based context service: there is no real user to resolve, and
        // the synthetic one has to be available in background jobs too.
        services.RemoveAll<IUserContextService>();
        services.AddSingleton<IUserContextService, NoneUserContextService>();
        services.AddSingleton<IUserInfoService, NoneUserInfoService>();

        return AuthProvider.None;
    }

    private static AuthProvider AddKeycloakAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string? authority = configuration["Authentication:Keycloak:Authority"];

        if (string.IsNullOrWhiteSpace(authority))
        {
            throw new InvalidOperationException(
                "Authentication:Provider is 'Keycloak' but Authentication:Keycloak:Authority is not set.");
        }

        services.AddSingleton<IAuthProviderService>(
            new AuthProviderService(AuthProvider.Keycloak, authority));

        services.AddScoped<IUserInfoService, KeycloakUserInfoService>();
        services.AddHttpClient();

        string? publicOrigin = configuration["Authentication:Keycloak:PublicOrigin"];
        string? backchannelBase = configuration["Authentication:Keycloak:BackchannelBaseUrl"];

        services.AddAuthentication(options =>
            {
                // The cookie holds the session; OIDC is only used to establish it.
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
            {
                options.LoginPath = "/auth/login";
                options.AccessDeniedPath = "/access-denied";
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromDays(7);
            })
            .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
            {
                options.Authority = authority;

                if (!string.IsNullOrWhiteSpace(backchannelBase))
                {
                    // In Docker the browser reaches Keycloak on a published address while the
                    // app reaches it on the compose network name. Discovery and token calls
                    // must use the internal address; redirects must use the external one.
                    options.BackchannelHttpHandler = new KeycloakBackchannelHandler(
                        authority,
                        backchannelBase);
                }

                options.ClientId = configuration["Authentication:Keycloak:ClientId"];
                options.ClientSecret = configuration["Authentication:Keycloak:ClientSecret"];
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.SaveTokens = true;
                options.GetClaimsFromUserInfoEndpoint = true;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    NameClaimType = "preferred_username",
                    RoleClaimType = ClaimTypes.Role,
                };

                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");

                options.NonceCookie.SameSite = SameSiteMode.Lax;
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;

                // Keycloak does not advertise PAR support consistently across versions, and
                // enabling it opportunistically breaks the code flow on older realms.
                options.PushedAuthorizationBehavior = PushedAuthorizationBehavior.Disable;

                options.Events = new OpenIdConnectEvents
                {
                    OnRedirectToIdentityProvider = context =>
                    {
                        if (!string.IsNullOrWhiteSpace(publicOrigin))
                        {
                            context.ProtocolMessage.RedirectUri = RewriteOrigin(
                                context.ProtocolMessage.RedirectUri,
                                publicOrigin);
                        }

                        return Task.CompletedTask;
                    },

                    // Sign-out needs the same rewrite as sign-in: behind a reverse proxy, the
                    // request's own scheme/host does not always match the address Keycloak's
                    // client has registered under "Valid post logout redirect URIs". Without
                    // this, Keycloak silently refuses to redirect back and the user is stuck on
                    // Keycloak's page — the local cookie is already cleared by then, so signing
                    // in again looks like nothing happened until a second sign-out completes
                    // the round trip.
                    OnRedirectToIdentityProviderForSignOut = context =>
                    {
                        if (!string.IsNullOrWhiteSpace(publicOrigin))
                        {
                            context.ProtocolMessage.PostLogoutRedirectUri = RewriteOrigin(
                                context.ProtocolMessage.PostLogoutRedirectUri,
                                publicOrigin);
                        }

                        return Task.CompletedTask;
                    },

                    OnTokenValidated = context =>
                    {
                        // Keycloak puts realm roles in a nested "realm_access.roles" claim,
                        // which ASP.NET does not understand. Without flattening them, every
                        // IsInRole("Administrator") check would fail and the app would look
                        // like it had lost its admin.
                        MapKeycloakRoles(context.Principal);
                        EnsureNameIdentifier(context.Principal);

                        return Task.CompletedTask;
                    },

                    OnUserInformationReceived = context =>
                    {
                        // Userinfo is merged after token validation and can drop the claims we
                        // just copied. Re-apply so Blazor still sees a NameIdentifier and roles.
                        MapKeycloakRoles(context.Principal);
                        EnsureNameIdentifier(context.Principal);

                        return Task.CompletedTask;
                    },

                    OnRemoteFailure = context =>
                    {
                        context.Response.Redirect("/access-denied");
                        context.HandleResponse();

                        return Task.CompletedTask;
                    },
                };
            });

        return AuthProvider.Keycloak;
    }

    /// <summary>
    /// Copies Keycloak's <c>realm_access.roles</c> and <c>resource_access.*.roles</c> entries
    /// onto <see cref="ClaimTypes.Role"/> so standard role checks work.
    /// </summary>
    private static void MapKeycloakRoles(ClaimsPrincipal? principal)
    {
        if (principal?.Identity is not ClaimsIdentity identity)
        {
            return;
        }

        var roleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var claim in identity.FindAll("realm_access").Concat(identity.FindAll("resource_access")))
        {
            try
            {
                using var document = JsonDocument.Parse(claim.Value);
                CollectRoles(document.RootElement, roleNames);
            }
            catch (JsonException)
            {
                // A non-JSON claim by that name isn't something we can use; ignore it rather
                // than failing the sign-in.
            }
        }

        foreach (string role in roleNames)
        {
            if (!identity.HasClaim(ClaimTypes.Role, role)
                && !identity.HasClaim(identity.RoleClaimType, role))
            {
                identity.AddClaim(new Claim(identity.RoleClaimType, role));
                if (identity.RoleClaimType != ClaimTypes.Role)
                {
                    identity.AddClaim(new Claim(ClaimTypes.Role, role));
                }
            }
        }
    }

    /// <summary>
    /// Keycloak's stable user id is <c>sub</c>. Copy it onto NameIdentifier when inbound claim
    /// mapping did not, so access rules and <c>IUserContextService</c> see the same value.
    /// </summary>
    private static void EnsureNameIdentifier(ClaimsPrincipal? principal)
    {
        if (principal?.Identity is not ClaimsIdentity identity
            || identity.HasClaim(c => c.Type == ClaimTypes.NameIdentifier))
        {
            return;
        }

        string? sub = identity.FindFirst("sub")?.Value;
        if (!string.IsNullOrWhiteSpace(sub))
        {
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, sub));
        }
    }

    private static void CollectRoles(JsonElement element, HashSet<string> roleNames)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("roles", out var roles) && roles.ValueKind == JsonValueKind.Array)
            {
                foreach (var role in roles.EnumerateArray())
                {
                    if (role.GetString() is { Length: > 0 } name)
                    {
                        roleNames.Add(name);
                    }
                }
            }

            // resource_access is keyed by client id, so recurse one level to pick up per-client
            // roles as well as realm roles.
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    CollectRoles(property.Value, roleNames);
                }
            }
        }
    }

    private static string? RewriteOrigin(string? url, string publicOrigin)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        return Uri.TryCreate(publicOrigin, UriKind.Absolute, out var origin)
            ? new UriBuilder(uri) { Scheme = origin.Scheme, Host = origin.Host, Port = origin.Port }
                .Uri.ToString()
            : url;
    }
}
