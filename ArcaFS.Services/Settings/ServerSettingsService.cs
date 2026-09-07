using System.Globalization;
using Microsoft.Extensions.Caching.Memory;

namespace ArcaFS.Services.Settings;

public sealed class ServerSettingsService(
    ILogger<ServerSettingsService> logger,
    IUserContextService userContext,
    IRepository<ServerSetting> repository,
    IMemoryCache cache) : IServerSettingsService
{
    private const string CacheKey = "arcafs.settings.all";

    /// <summary>
    /// Settings change rarely and are read constantly, so they are cached as one dictionary and
    /// evicted wholesale on write rather than cached per key.
    /// </summary>
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var all = await LoadAsync(cancellationToken);
        return all.GetValueOrDefault(key);
    }

    public async Task<T> GetAsync<T>(string key, T fallback, CancellationToken cancellationToken = default)
    {
        string? raw = await GetAsync(key, cancellationToken);
        if (string.IsNullOrEmpty(raw))
        {
            return fallback;
        }

        try
        {
            // Booleans are stored as "true"/"false" and enums by name, both of which
            // ChangeType cannot handle, so they are converted explicitly.
            if (typeof(T) == typeof(bool) || typeof(T) == typeof(bool?))
            {
                return (T)(object)bool.Parse(raw);
            }

            if (typeof(T).IsEnum)
            {
                return (T)Enum.Parse(typeof(T), raw, ignoreCase: true);
            }

            return (T)Convert.ChangeType(raw, typeof(T), CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or ArgumentException)
        {
            logger.LogWarning(
                "Setting '{Key}' holds '{Value}', which is not a valid {Type}. Using the default.",
                key,
                raw,
                typeof(T).Name);

            return fallback;
        }
    }

    public async Task SetAsync(string key, string? value, CancellationToken cancellationToken = default)
    {
        var existing = await repository.FindOneAsync(new SearchOptions<ServerSetting>
        {
            Query = s => s.Key == key,
            CancellationToken = cancellationToken,
        });

        if (existing is null)
        {
            await repository.InsertAsync(new ServerSetting
            {
                Key = key,
                Value = value,
                UpdatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            existing.Value = value;
            existing.UpdatedAt = DateTime.UtcNow;
            await repository.UpdateAsync(existing);
        }

        cache.Remove(CacheKey);
    }

    public async Task<ServerSettingsDto> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var all = await LoadAsync(cancellationToken);

        return new ServerSettingsDto
        {
            Theme = all.GetValueOrDefault(Constants.ServerSettingKeys.Theme)
                is { Length: > 0 } theme
                ? theme
                : Constants.ServerSettingDefaults.Theme,

            SiteName = all.GetValueOrDefault(Constants.ServerSettingKeys.SiteName)
                is { Length: > 0 } siteName
                ? siteName
                : "ArcaFS",

            SetupComplete = ParseBool(all.GetValueOrDefault(Constants.ServerSettingKeys.SetupComplete)),
            AllowRegistration = ParseBool(all.GetValueOrDefault(Constants.ServerSettingKeys.AllowRegistration)),
            SharesEnabled = ParseBool(all.GetValueOrDefault(Constants.ServerSettingKeys.SharesEnabled), true),
        };
    }

    public async Task<Result> SaveAsync(
        ServerSettingsDto settings,
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        if (!Constants.BootswatchThemes.All.Contains(settings.Theme, StringComparer.OrdinalIgnoreCase))
        {
            return Result.Invalid(new ValidationError(
                nameof(settings.Theme), $"'{settings.Theme}' is not one of the available themes."));
        }

        string siteName = settings.SiteName?.Trim() ?? string.Empty;
        if (siteName.Length == 0)
        {
            return Result.Invalid(new ValidationError(nameof(settings.SiteName), "Enter a site name."));
        }

        await SetAsync(Constants.ServerSettingKeys.Theme, settings.Theme, cancellationToken);
        await SetAsync(Constants.ServerSettingKeys.SiteName, siteName, cancellationToken);
        await SetAsync(
            Constants.ServerSettingKeys.AllowRegistration,
            settings.AllowRegistration.ToString(),
            cancellationToken);
        await SetAsync(
            Constants.ServerSettingKeys.SharesEnabled,
            settings.SharesEnabled.ToString(),
            cancellationToken);

        logger.LogInformation("Server settings updated by {User}", userContext.GetCurrentUserName());

        return Result.Success();
    }

    public async Task<bool> IsSetupCompleteAsync(CancellationToken cancellationToken = default) =>
        ParseBool(await GetAsync(Constants.ServerSettingKeys.SetupComplete, cancellationToken));

    public Task MarkSetupCompleteAsync(CancellationToken cancellationToken = default) =>
        SetAsync(Constants.ServerSettingKeys.SetupComplete, bool.TrueString, cancellationToken);

    private async Task<Dictionary<string, string?>> LoadAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey, out Dictionary<string, string?>? cached) && cached is not null)
        {
            return cached;
        }

        Dictionary<string, string?> map;

        try
        {
            var rows = await repository.FindAsync(new SearchOptions<ServerSetting>
            {
                CancellationToken = cancellationToken,
            });

            map = rows.ToDictionary(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            // Before the first migration runs the table does not exist yet, and the setup
            // wizard needs to render anyway. Treating that as "no settings" is correct.
            logger.LogDebug(ex, "Server settings could not be read; falling back to defaults");
            return [];
        }

        cache.Set(CacheKey, map, CacheDuration);

        return map;
    }

    private static bool ParseBool(string? value, bool fallback = false) =>
        bool.TryParse(value, out bool parsed) ? parsed : fallback;
}
