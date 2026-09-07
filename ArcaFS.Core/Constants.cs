namespace ArcaFS;

public static class Constants
{
    /// <summary>Browser <c>localStorage</c> key for the per-user Bootswatch theme (client-only).</summary>
    public const string ClientThemeLocalStorageKey = "arcafs.theme";

    /// <summary>Browser <c>localStorage</c> key for the per-user file listing view mode.</summary>
    public const string ClientViewModeLocalStorageKey = "arcafs.viewMode";

    /// <summary>
    /// Synthetic user id used by the "None" authentication mode (desktop / kiosk scenarios).
    /// </summary>
    public const string DefaultUserId = "_default";

    /// <summary>
    /// Display name used for the synthetic user when no real auth provider is active.
    /// </summary>
    public const string DefaultUserName = "Default User";

    /// <summary>
    /// Sentinel user id for anonymous share visitors. Never matches a real Identity user, so
    /// access rules targeting <c>Anonymous</c> are the only way to grant it anything.
    /// </summary>
    public const string AnonymousUserId = "_anonymous";

    /// <summary>
    /// Path separator for virtual (source-relative) paths. Always forward slash regardless of
    /// the host OS so rules written on Linux keep working on Windows and vice versa.
    /// </summary>
    public const char PathSeparator = '/';

    /// <summary>Virtual path of a source root.</summary>
    public const string RootPath = "/";

    /// <summary>
    /// Placeholder tokens expanded inside <c>AccessRule.Path</c> and <c>Source.DefaultUserScope</c>
    /// when a rule is evaluated for a specific principal. This is what lets one rule describe
    /// "everyone owns their own home directory" instead of one rule per user.
    /// </summary>
    public static class PathTokens
    {
        public const string UserName = "${username}";
        public const string UserId = "${userid}";
    }

    public static class AuthProviders
    {
        public const string Identity = "Identity";
        public const string Keycloak = "Keycloak";
        public const string None = "None";
    }

    public static class DatabaseProviders
    {
        public const string MySql = "MySql";
        public const string Npgsql = "Npgsql";
        public const string Sqlite = "Sqlite";
        public const string SqlServer = "SqlServer";
    }

    public static class HangfireQueues
    {
        public const string Critical = "critical";
        public const string Default = "default";
        public const string Index = "index";
        public const string Thumbnails = "thumbnails";
    }

    public static class Roles
    {
        public const string Administrator = "Administrator";
        public const string User = "User";
    }

    public static class Schemas
    {
        public const string App = "app";
    }

    /// <summary>
    /// Bootswatch themes shipped via LibMan to <c>wwwroot/lib/bootswatch/dist/&lt;theme&gt;/bootstrap.min.css</c>.
    /// Keep this list in lock-step with the <c>files</c> entries in <c>libman.json</c> — themes
    /// listed here but not restored produce 404s when selected.
    /// </summary>
    public static class BootswatchThemes
    {
        public static readonly IReadOnlyList<string> All =
        [
            "brite", "cerulean", "cosmo", "cyborg", "darkly", "flatly", "journal", "litera", "lumen",
            "lux", "materia", "minty", "morph", "pulse", "quartz", "sandstone", "simplex", "sketchy",
            "slate", "solar", "spacelab", "superhero", "united", "vapor", "yeti", "zephyr"
        ];

        /// <summary>
        /// Themes with a dark chrome by default. Bootswatch still keys off <c>data-bs-theme</c> to
        /// apply the <c>[data-bs-theme=dark]</c> variable overrides; without it,
        /// <c>--bs-emphasis-color</c> and <c>--bs-tertiary-bg</c> keep their light-theme values.
        /// </summary>
        public static readonly IReadOnlySet<string> DarkColorScheme =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "cyborg", "darkly", "slate", "solar", "superhero", "vapor"
            };

        public static bool IsDarkColorScheme(string? theme) =>
            !string.IsNullOrEmpty(theme) && DarkColorScheme.Contains(theme);

        public static IEnumerable<string> DarkThemes => All.Where(DarkColorScheme.Contains);

        public static IEnumerable<string> LightThemes => All.Where(t => !DarkColorScheme.Contains(t));
    }

    /// <summary>
    /// Default values applied when a setting has never been written. Centralised so the theme
    /// picker, the layout component and the Identity pages can't drift apart.
    /// </summary>
    public static class ServerSettingDefaults
    {
        public const string Theme = "flatly";
    }

    /// <summary>
    /// Well-known keys persisted in the <c>ServerSettings</c> table by
    /// <c>IServerSettingsService</c>. Keep the strings stable — they are the source of truth
    /// once written to the database.
    /// </summary>
    public static class ServerSettingKeys
    {
        public const string SetupComplete = "setup.complete";
        public const string Theme = "ui.theme";
        public const string SiteName = "ui.siteName";
        public const string AllowRegistration = "auth.allowRegistration";
        public const string SharesEnabled = "shares.enabled";
    }

    /// <summary>
    /// Upload chunking contract between <c>wwwroot/js/arcafs.upload.js</c> and
    /// <c>FilesController</c>. Chunked uploads let a browser resume a large transfer without
    /// re-sending bytes the server already has.
    /// </summary>
    public static class UploadHeaders
    {
        public const string ChunkOffset = "X-ArcaFS-Chunk-Offset";
        public const string TotalSize = "X-ArcaFS-Total-Size";
        public const string UploadSession = "X-ArcaFS-Upload-Session";
    }

    /// <summary>Suffix used for in-progress upload temp files so the indexer can skip them.</summary>
    public const string UploadTempSuffix = ".arcafs-upload.tmp";

    public static class MaxLengths
    {
        public const int SourceName = 128;
        public const int Path = 1024;
        public const int SettingKey = 128;
        public const int GroupName = 128;
        public const int ShareToken = 64;
    }
}
