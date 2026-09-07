namespace ArcaFS.Infrastructure;

/// <summary>
/// Configuration lookups that more than one part of the host needs to agree on.
/// </summary>
public static class ArcaFsConfiguration
{
    /// <summary>
    /// The configured database provider, normalised, defaulting to SQLite.
    /// </summary>
    /// <remarks>
    /// The DI registration, the Hangfire storage and the Serilog sink all have to pick the same
    /// provider, and the desktop build overrides it before the host is built. One accessor keeps
    /// them from disagreeing.
    /// </remarks>
    public static string ResolveDatabaseProvider(IConfiguration configuration) =>
        configuration["Database:Provider"] is { Length: > 0 } provider
            ? provider.Trim()
            : Constants.DatabaseProviders.Sqlite;
}
