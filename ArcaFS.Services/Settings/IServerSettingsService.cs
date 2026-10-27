namespace ArcaFS.Services.Settings;

/// <summary>
/// Instance-wide settings held as key/value rows, cached in memory because the layout reads the
/// theme and site name on every render.
/// </summary>
public interface IServerSettingsService
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task<T> GetAsync<T>(string key, T fallback, CancellationToken cancellationToken = default);

    Task SetAsync(string key, string? value, CancellationToken cancellationToken = default);

    Task<ServerSettingsDto> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Administrator-only bulk update from the settings page.</summary>
    Task<Result> SaveAsync(ServerSettingsDto settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the first-run wizard has been completed. Read on every request by the setup
    /// redirect middleware, so it must stay cheap.
    /// </summary>
    Task<bool> IsSetupCompleteAsync(CancellationToken cancellationToken = default);

    Task MarkSetupCompleteAsync(CancellationToken cancellationToken = default);
}