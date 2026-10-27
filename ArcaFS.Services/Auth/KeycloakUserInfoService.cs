using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace ArcaFS.Services.Auth;

/// <summary>
/// Reads users from Keycloak's Admin REST API using a client-credentials token, so the access
/// rules editor can offer real names for the user ids it stores.
/// <para>
/// Every failure degrades to an empty result and a warning rather than throwing: an unreachable
/// IdP should make the rules editor show bare ids, not take the whole page down.
/// </para>
/// </summary>
public sealed class KeycloakUserInfoService(
    ILogger<KeycloakUserInfoService> logger,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : IUserInfoService
{
    private const int MaxUsers = 1000;

    private static readonly Regex RealmPattern = new(
        @"/realms/([^/?#]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));

    private string? cachedToken;
    private DateTimeOffset tokenExpiresAt = DateTimeOffset.MinValue;

    public async Task<IReadOnlyDictionary<string, UserInfo>> GetUserInfoAsync(
        IEnumerable<string> userIds,
        CancellationToken cancellationToken = default)
    {
        var ids = userIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.Ordinal);
        if (ids.Count == 0)
        {
            return new Dictionary<string, UserInfo>();
        }

        var all = await GetAllUsersAsync(cancellationToken);
        return all
            .Where(u => ids.Contains(u.Id))
            .ToDictionary(u => u.Id, StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<UserInfo>> GetAllUsersAsync(CancellationToken cancellationToken = default)
    {
        var (baseUrl, realm) = ResolveRealm();
        if (baseUrl is null || realm is null)
        {
            return [];
        }

        string? token = await GetAdminTokenAsync(baseUrl, realm, cancellationToken);
        if (token is null)
        {
            return [];
        }

        try
        {
            using var client = httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await client.GetAsync(
                $"{baseUrl}/admin/realms/{realm}/users?max={MaxUsers}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // 403 here almost always means the service account is missing a
                // realm-management role. Listing needs both view-users and query-users —
                // Keycloak treats "search for users" and "see user details" as separate grants.
                string body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning(
                    "Keycloak user listing failed with {StatusCode} for realm '{Realm}'. The service "
                        + "account needs the realm-management roles view-users AND query-users. Response: {Body}",
                    (int)response.StatusCode,
                    realm,
                    Truncate(body));
                return [];
            }

            var users = await response.Content.ReadFromJsonAsync<List<KeycloakUser>>(cancellationToken);

            return users is null
                ? []
                : users
                    .Where(u => u.Id is not null)
                    .Select(u => new UserInfo(
                        u.Id!,
                        u.Username,
                        u.Email,
                        BuildDisplayName(u),
                        Roles: []))
                    .OrderBy(u => u.UserName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not list users from Keycloak realm '{Realm}'", realm);
            return [];
        }
    }

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500];

    public async Task<UserInfo?> GetUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var all = await GetAllUsersAsync(cancellationToken);
        return all.FirstOrDefault(u => u.Id == userId);
    }

    /// <summary>
    /// Splits <c>Authentication:Keycloak:Authority</c> into its server base URL and realm.
    /// Prefers <c>BackchannelBaseUrl</c> when set, which is how a container reaches Keycloak on
    /// the internal Docker network while the browser uses the public hostname.
    /// </summary>
    private (string? BaseUrl, string? Realm) ResolveRealm()
    {
        string? authority = configuration["Authentication:Keycloak:Authority"];
        if (string.IsNullOrWhiteSpace(authority))
        {
            return (null, null);
        }

        var match = RealmPattern.Match(authority);
        if (!match.Success)
        {
            logger.LogWarning("Keycloak authority '{Authority}' has no /realms/{{realm}} segment", authority);
            return (null, null);
        }

        string realm = match.Groups[1].Value;
        string? backchannel = configuration["Authentication:Keycloak:BackchannelBaseUrl"];
        string baseUrl = string.IsNullOrWhiteSpace(backchannel)
            ? authority[..match.Index]
            : backchannel;

        return (baseUrl.TrimEnd('/'), realm);
    }

    private async Task<string?> GetAdminTokenAsync(string baseUrl, string realm, CancellationToken cancellationToken)
    {
        if (cachedToken is not null && tokenExpiresAt > DateTimeOffset.UtcNow.AddSeconds(30))
        {
            return cachedToken;
        }

        string? clientId = configuration["Authentication:Keycloak:AdminClientId"]
            ?? configuration["Authentication:Keycloak:ClientId"];
        string? clientSecret = configuration["Authentication:Keycloak:AdminClientSecret"]
            ?? configuration["Authentication:Keycloak:ClientSecret"];

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            logger.LogWarning(
                "Keycloak user listing needs AdminClientId/AdminClientSecret (or ClientId/ClientSecret) with "
                    + "the realm-management roles view-users and query-users");
            return null;
        }

        try
        {
            using var client = httpClientFactory.CreateClient();
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
            });

            using var response = await client.PostAsync(
                $"{baseUrl}/realms/{realm}/protocol/openid-connect/token",
                content,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning(
                    "Keycloak token request failed with {StatusCode} for realm '{Realm}'. Response: {Body}",
                    (int)response.StatusCode,
                    realm,
                    Truncate(body));
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            if (!payload.TryGetProperty("access_token", out var accessToken))
            {
                return null;
            }

            int expiresIn = payload.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 60;
            cachedToken = accessToken.GetString();
            tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);

            return cachedToken;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not obtain a Keycloak admin token for realm '{Realm}'", realm);
            return null;
        }
    }

    private static string? BuildDisplayName(KeycloakUser user)
    {
        string full = string.Join(' ', new[] { user.FirstName, user.LastName }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        return full.Length == 0 ? user.Username : full;
    }

    private sealed record KeycloakUser
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public string? Id { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("username")]
        public string? Username { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("email")]
        public string? Email { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("firstName")]
        public string? FirstName { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("lastName")]
        public string? LastName { get; init; }
    }
}