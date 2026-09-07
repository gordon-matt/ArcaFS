using ArcaFS.Enums;

namespace ArcaFS;

/// <summary>
/// Extension-driven classification of files into <see cref="FileKind"/>, plus the MIME type and
/// Bootstrap icon that go with it. Centralised here so the listing icons, the preview router and
/// the <c>type:</c> search filters can never disagree about what an <c>.mkv</c> is.
/// </summary>
public static class FileKinds
{
    private static readonly Dictionary<string, FileKind> KindByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = FileKind.Image,
        [".jpeg"] = FileKind.Image,
        [".png"] = FileKind.Image,
        [".gif"] = FileKind.Image,
        [".bmp"] = FileKind.Image,
        [".webp"] = FileKind.Image,
        [".svg"] = FileKind.Image,
        [".ico"] = FileKind.Image,
        [".tif"] = FileKind.Image,
        [".tiff"] = FileKind.Image,
        [".avif"] = FileKind.Image,
        [".heic"] = FileKind.Image,

        [".mp4"] = FileKind.Video,
        [".webm"] = FileKind.Video,
        [".mkv"] = FileKind.Video,
        [".avi"] = FileKind.Video,
        [".mov"] = FileKind.Video,
        [".wmv"] = FileKind.Video,
        [".flv"] = FileKind.Video,
        [".m4v"] = FileKind.Video,
        [".mpg"] = FileKind.Video,
        [".mpeg"] = FileKind.Video,

        [".mp3"] = FileKind.Audio,
        [".flac"] = FileKind.Audio,
        [".wav"] = FileKind.Audio,
        [".ogg"] = FileKind.Audio,
        [".oga"] = FileKind.Audio,
        [".opus"] = FileKind.Audio,
        [".m4a"] = FileKind.Audio,
        [".aac"] = FileKind.Audio,
        [".wma"] = FileKind.Audio,

        [".pdf"] = FileKind.Pdf,
        [".epub"] = FileKind.Epub,

        [".doc"] = FileKind.Document,
        [".docx"] = FileKind.Document,
        [".xls"] = FileKind.Document,
        [".xlsx"] = FileKind.Document,
        [".ppt"] = FileKind.Document,
        [".pptx"] = FileKind.Document,
        [".odt"] = FileKind.Document,
        [".ods"] = FileKind.Document,
        [".odp"] = FileKind.Document,
        [".rtf"] = FileKind.Document,

        [".zip"] = FileKind.Archive,
        [".tar"] = FileKind.Archive,
        [".gz"] = FileKind.Archive,
        [".tgz"] = FileKind.Archive,
        [".bz2"] = FileKind.Archive,
        [".xz"] = FileKind.Archive,
        [".7z"] = FileKind.Archive,
        [".rar"] = FileKind.Archive,

        [".txt"] = FileKind.Text,
        [".md"] = FileKind.Text,
        [".log"] = FileKind.Text,
        [".csv"] = FileKind.Text,
        [".nfo"] = FileKind.Text,

        [".cs"] = FileKind.Code,
        [".js"] = FileKind.Code,
        [".mjs"] = FileKind.Code,
        [".ts"] = FileKind.Code,
        [".jsx"] = FileKind.Code,
        [".tsx"] = FileKind.Code,
        [".json"] = FileKind.Code,
        [".xml"] = FileKind.Code,
        [".yml"] = FileKind.Code,
        [".yaml"] = FileKind.Code,
        [".toml"] = FileKind.Code,
        [".ini"] = FileKind.Code,
        [".conf"] = FileKind.Code,
        [".sh"] = FileKind.Code,
        [".ps1"] = FileKind.Code,
        [".bat"] = FileKind.Code,
        [".py"] = FileKind.Code,
        [".rb"] = FileKind.Code,
        [".go"] = FileKind.Code,
        [".rs"] = FileKind.Code,
        [".java"] = FileKind.Code,
        [".kt"] = FileKind.Code,
        [".php"] = FileKind.Code,
        [".sql"] = FileKind.Code,
        [".html"] = FileKind.Code,
        [".htm"] = FileKind.Code,
        [".css"] = FileKind.Code,
        [".scss"] = FileKind.Code,
        [".razor"] = FileKind.Code,
        [".vue"] = FileKind.Code,
        [".c"] = FileKind.Code,
        [".h"] = FileKind.Code,
        [".cpp"] = FileKind.Code,
        [".hpp"] = FileKind.Code,
    };

    private static readonly Dictionary<string, string> MimeByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".gif"] = "image/gif",
        [".bmp"] = "image/bmp",
        [".webp"] = "image/webp",
        [".svg"] = "image/svg+xml",
        [".ico"] = "image/x-icon",
        [".tif"] = "image/tiff",
        [".tiff"] = "image/tiff",
        [".avif"] = "image/avif",
        [".heic"] = "image/heic",

        [".mp4"] = "video/mp4",
        [".webm"] = "video/webm",
        [".mkv"] = "video/x-matroska",
        [".avi"] = "video/x-msvideo",
        [".mov"] = "video/quicktime",
        [".m4v"] = "video/x-m4v",
        [".mpg"] = "video/mpeg",
        [".mpeg"] = "video/mpeg",

        [".mp3"] = "audio/mpeg",
        [".flac"] = "audio/flac",
        [".wav"] = "audio/wav",
        [".ogg"] = "audio/ogg",
        [".oga"] = "audio/ogg",
        [".opus"] = "audio/opus",
        [".m4a"] = "audio/mp4",
        [".aac"] = "audio/aac",

        [".pdf"] = "application/pdf",
        [".epub"] = "application/epub+zip",

        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",

        [".zip"] = "application/zip",
        [".tar"] = "application/x-tar",
        [".gz"] = "application/gzip",
        [".7z"] = "application/x-7z-compressed",
        [".rar"] = "application/vnd.rar",

        [".txt"] = "text/plain",
        [".md"] = "text/markdown",
        [".log"] = "text/plain",
        [".csv"] = "text/csv",
        [".json"] = "application/json",
        [".xml"] = "application/xml",
        [".html"] = "text/html",
        [".htm"] = "text/html",
        [".css"] = "text/css",
        [".js"] = "text/javascript",
    };

    public const string DefaultMimeType = "application/octet-stream";

    /// <summary>
    /// Text-ish kinds the in-browser editor will open. Everything else is treated as binary
    /// even when it happens to be readable, so we don't corrupt a file by saving it as UTF-8.
    /// </summary>
    public static bool IsEditable(FileKind kind) => kind is FileKind.Text or FileKind.Code;

    /// <summary>Kinds the in-app viewer can show, whether in a dialog or on a page.</summary>
    public static bool CanPreview(FileKind kind) => kind is
        FileKind.Image or FileKind.Video or FileKind.Audio or FileKind.Pdf
        or FileKind.Text or FileKind.Code;

    /// <summary>
    /// Whether a download may be served with <c>Content-Disposition: inline</c>.
    /// </summary>
    /// <remarks>
    /// This is an allow-list on purpose. Rendering user-supplied content inline on the app's own
    /// origin turns any upload into stored XSS, so <c>text/html</c>, <c>image/svg+xml</c> and
    /// anything unrecognised are always sent as attachments. Only media the browser decodes
    /// rather than executes, plus PDF, is listed.
    /// </remarks>
    public static bool IsInlineSafe(string? mimeType)
    {
        if (string.IsNullOrEmpty(mimeType))
        {
            return false;
        }

        if (mimeType.Equals("image/svg+xml", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            || mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
            || mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)
            || mimeType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)
            || mimeType.Equals("text/plain", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Syntax-highlighting mode name for the in-browser editor, keyed off the extension.
    /// Unknown extensions fall back to plain text rather than guessing.
    /// </summary>
    public static string GetEditorLanguage(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".cs" => "csharp",
            ".js" or ".mjs" or ".jsx" => "javascript",
            ".ts" or ".tsx" => "typescript",
            ".json" => "json",
            ".xml" or ".csproj" or ".props" or ".targets" => "xml",
            ".yml" or ".yaml" => "yaml",
            ".toml" => "toml",
            ".ini" or ".conf" => "ini",
            ".sh" => "shell",
            ".ps1" => "powershell",
            ".bat" => "bat",
            ".py" => "python",
            ".rb" => "ruby",
            ".go" => "go",
            ".rs" => "rust",
            ".java" => "java",
            ".kt" => "kotlin",
            ".php" => "php",
            ".sql" => "sql",
            ".html" or ".htm" or ".razor" or ".vue" => "html",
            ".css" => "css",
            ".scss" => "scss",
            ".c" or ".h" => "c",
            ".cpp" or ".hpp" => "cpp",
            ".md" => "markdown",
            _ => "plaintext",
        };

    public static FileKind Classify(string fileName, bool isDirectory)
    {
        if (isDirectory)
        {
            return FileKind.Folder;
        }

        string extension = Path.GetExtension(fileName);
        return string.IsNullOrEmpty(extension)
            ? FileKind.Other
            : KindByExtension.GetValueOrDefault(extension, FileKind.Other);
    }

    public static string GetMimeType(string fileName)
    {
        string extension = Path.GetExtension(fileName);
        return string.IsNullOrEmpty(extension)
            ? DefaultMimeType
            : MimeByExtension.GetValueOrDefault(extension, DefaultMimeType);
    }

    /// <summary>Bootstrap Icons class for an item, used by the listing and breadcrumbs.</summary>
    public static string GetIcon(FileKind kind) => kind switch
    {
        FileKind.Folder => "bi-folder-fill",
        FileKind.Image => "bi-file-earmark-image",
        FileKind.Video => "bi-file-earmark-play",
        FileKind.Audio => "bi-file-earmark-music",
        FileKind.Pdf => "bi-file-earmark-pdf",
        FileKind.Epub => "bi-book",
        FileKind.Document => "bi-file-earmark-word",
        FileKind.Archive => "bi-file-earmark-zip",
        FileKind.Text => "bi-file-earmark-text",
        FileKind.Code => "bi-file-earmark-code",
        _ => "bi-file-earmark",
    };

    /// <summary>Formats a byte count for display (1 KB = 1024 B).</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes < 0)
        {
            return "-";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes} {units[unit]}"
            : $"{value:0.##} {units[unit]}";
    }
}
