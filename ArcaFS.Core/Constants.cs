using ArcaFS.Enums;

namespace ArcaFS;

/// <summary>Stable strings and limits shared by the host, the services, and the browser script.</summary>
public static class Constants
{
    /// <summary>Browser <c>localStorage</c> key for the light or dark scheme (client-only).</summary>
    public const string ClientThemeLocalStorageKey = "arcafs.theme";

    /// <summary>Browser <c>localStorage</c> key for the per-user file listing view mode.</summary>
    public const string ClientViewModeLocalStorageKey = "arcafs.viewMode";

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
    /// <summary>Tokens substituted in a rule path or a user's starting folder.</summary>
    public static class PathTokens
    {
        /// <summary>Replaced with the principal's folder name.</summary>
        public const string UserName = "${username}";

        /// <summary>Replaced with the principal's user id.</summary>
        public const string UserId = "${userid}";
    }

    /// <summary>Hangfire queue names.</summary>
    public static class HangfireQueues
    {
        /// <summary>Queue on the main server, processed after <see cref="Default"/>.</summary>
        public const string Critical = "critical";

        /// <summary>
        /// Queue on the main server, processed before <see cref="Critical"/>.
        /// The nightly activity purge runs here.
        /// </summary>
        public const string Default = "default";

        /// <summary>
        /// Index-scan queue. It has its own single-worker server, so a long scan does not
        /// block the main server.
        /// </summary>
        public const string Index = "index";

        /// <summary>Thumbnail queue. It has its own server, separate from indexing.</summary>
        public const string Thumbnails = "thumbnails";
    }

    /// <summary>Database schema names.</summary>
    public static class Schemas
    {
        /// <summary>Schema the application tables use.</summary>
        public const string App = "app";
    }

    /// <summary>
    /// Default values applied when a setting has never been written. Centralised so the theme
    /// picker, the layout component and the Identity pages can't drift apart.
    /// </summary>
    public static class ServerSettingDefaults
    {
        public const string Theme = "system";
        public const FileOpenTarget OpenFilesIn = FileOpenTarget.Dialog;
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
        public const string OpenFilesIn = "ui.openFilesIn";
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
        /// <summary>Header carrying the byte offset of this upload chunk.</summary>
        public const string ChunkOffset = "X-ArcaFS-Chunk-Offset";

        /// <summary>Header carrying the finished file's size.</summary>
        public const string TotalSize = "X-ArcaFS-Total-Size";

        /// <summary>Header carrying the id that ties one file's chunks together.</summary>
        public const string UploadSession = "X-ArcaFS-Upload-Session";
    }

    /// <summary>Suffix used for in-progress upload temp files so the indexer can skip them.</summary>
    public const string UploadTempSuffix = ".arcafs-upload.tmp";

    /// <summary>Column lengths enforced by the database.</summary>
    public static class MaxLengths
    {
        /// <summary>Source name.</summary>
        public const int SourceName = 128;

        /// <summary>Virtual or host path.</summary>
        public const int Path = 1024;

        /// <summary>Server setting key.</summary>
        public const int SettingKey = 128;

        /// <summary>Access group name.</summary>
        public const int GroupName = 128;

        /// <summary>Public share token.</summary>
        public const int ShareToken = 64;
    }
}