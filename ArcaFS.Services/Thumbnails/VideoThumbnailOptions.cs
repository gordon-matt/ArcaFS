namespace ArcaFS.Services.Thumbnails;

/// <summary>
/// How video thumbnails find ffmpeg. Frame capture stays off when the binaries are missing,
/// and nothing is downloaded unless <see cref="DownloadIfMissing"/> is set, because the official
/// build is GPL-licensed.
/// </summary>
public sealed class VideoThumbnailOptions
{
    public const string SectionName = "Thumbnails:Video";

    /// <summary>When false, videos keep their normal icon even if ffmpeg is installed.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Where an official build is stored when <see cref="DownloadIfMissing"/> is set.
    /// Empty uses an <c>ffmpeg</c> folder under the content root.
    /// </summary>
    public string? DownloadDirectory { get; set; }

    /// <summary>
    /// Download the official ffmpeg build when nothing local is complete. That build is
    /// GPL-licensed, so this stays off until the host opts in.
    /// </summary>
    public bool DownloadIfMissing { get; set; }

    /// <summary>A file path or a directory that already contains ffmpeg and ffprobe.</summary>
    public string? FfmpegPath { get; set; }

    /// <summary>On Linux, check <c>/usr/bin</c> before any other location.</summary>
    public bool PreferLinuxSystemDirectory { get; set; } = true;

    /// <summary>Also look next to the app and in the process working directory.</summary>
    public bool SearchApplicationDirectories { get; set; } = true;

    /// <summary>Also look through the process PATH.</summary>
    public bool SearchPath { get; set; } = true;

    /// <summary>Look in Program Files, the user profile, and the usual Linux and macOS bin directories.</summary>
    public bool SearchWellKnownLocations { get; set; } = true;
}
