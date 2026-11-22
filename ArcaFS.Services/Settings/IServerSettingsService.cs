namespace ArcaFS.Services.Settings;

/// <summary>
/// Instance-wide settings held as key/value rows, cached in memory because the layout reads the
/// theme and site name on every render.
/// </summary>
public interface IServerSettingsService
{
    /// <summary>The stored text for one key, or <see langword="null"/> when it has never been set.</summary>
    /// <param name="key">Setting key.</param>
    /// <param name="cancellationToken">Cancels the load when the cache is cold.</param>
    /// <returns>The stored text, or <see langword="null"/>.</returns>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// The stored value converted to <typeparamref name="T"/>. Booleans are parsed from
    /// <c>true</c> and <c>false</c>, and enums by name, ignoring case. A missing or unparseable
    /// value returns <paramref name="fallback"/>.
    /// </summary>
    /// <typeparam name="T">Type to convert the stored text to.</typeparam>
    /// <param name="key">Setting key.</param>
    /// <param name="fallback">Value used when the key is missing or the text does not convert.</param>
    /// <param name="cancellationToken">Cancels the load when the cache is cold.</param>
    /// <returns>The converted value, or <paramref name="fallback"/>.</returns>
    Task<T> GetAsync<T>(string key, T fallback, CancellationToken cancellationToken = default);

    /// <summary>Inserts or replaces one setting and drops the cache.</summary>
    /// <param name="key">Setting key.</param>
    /// <param name="value">Text to store. <see langword="null"/> clears the value.</param>
    /// <param name="cancellationToken">Cancels the lookup. The write is not cancelled once it has started.</param>
    /// <returns>A completed task once the row is stored.</returns>
    Task SetAsync(string key, string? value, CancellationToken cancellationToken = default);

    /// <summary>
    /// The settings the admin page edits. A missing site name reads as ArcaFS. Shares default
    /// to enabled. A stored theme of light, dark, or system is kept; another dark scheme reads
    /// as dark, and anything else reads as the default scheme.
    /// </summary>
    /// <param name="cancellationToken">Cancels the load when the cache is cold.</param>
    /// <returns>The current settings.</returns>
    Task<ServerSettingsDto> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Administrator-only bulk update from the settings page.</summary>
    /// <param name="settings">Theme, site name, registration, shares, and where files open. The site name is trimmed.</param>
    /// <param name="cancellationToken">Cancels each setting write.</param>
    /// <returns>
    /// Success, or a failure when the caller is not an administrator, the theme is not light,
    /// dark, or system, or the site name is blank.
    /// </returns>
    Task<Result> SaveAsync(ServerSettingsDto settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the first-run wizard has been completed. Read on every request by the setup
    /// redirect middleware, so it must stay cheap.
    /// </summary>
    /// <param name="cancellationToken">Cancels the load when the cache is cold.</param>
    /// <returns><see langword="true"/> when setup has been marked complete.</returns>
    Task<bool> IsSetupCompleteAsync(CancellationToken cancellationToken = default);

    /// <summary>Marks the first-run wizard complete.</summary>
    /// <param name="cancellationToken">Cancels the write the same way <see cref="SetAsync"/> does.</param>
    /// <returns>A completed task once the flag is stored.</returns>
    Task MarkSetupCompleteAsync(CancellationToken cancellationToken = default);
}
