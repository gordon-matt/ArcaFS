using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace ArcaFS.Services.Thumbnails.Ffmpeg;

/// <summary>Where ffmpeg and ffprobe were found. Xabe runs only when both binaries are in one directory.</summary>
internal sealed record FfmpegLocation(string Directory, string FfmpegPath, string FfprobePath);

/// <summary>How to find or download ffmpeg. The host fills in the directory it wants to use.</summary>
internal sealed class FfmpegBootstrapOptions
{
    public string? ConfiguredPath { get; init; }

    public string? DownloadDirectory { get; init; }

    public bool SearchWellKnownLocations { get; init; } = true;

    public bool SearchApplicationDirectories { get; init; }

    public bool SearchPath { get; init; } = true;

    public bool PreferLinuxSystemDirectory { get; init; }

    /// <summary>
    /// Download the official build when nothing local is complete. That build is GPL-licensed,
    /// so the host opts in.
    /// </summary>
    public bool DownloadIfMissing { get; init; }

    public bool RequireFfprobe { get; init; } = true;
}

/// <summary>
/// Finds ffmpeg and ffprobe, downloading an official build only when the host asked for that.
/// A missing or failed download leaves <see cref="IsAvailable"/> false and does not throw.
/// </summary>
internal static class FfmpegBootstrap
{
    private static volatile bool isAvailable;

    public static bool IsAvailable => isAvailable;

    public static async Task<FfmpegLocation?> EnsureAsync(
        FfmpegBootstrapOptions options,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            FfmpegLocation? resolved = Resolve(options);
            if (resolved is not null)
            {
                Use(resolved);
                logger?.LogInformation("FFmpeg tools directory: {Path}", resolved.Directory);
                return resolved;
            }

            if (!options.DownloadIfMissing || string.IsNullOrWhiteSpace(options.DownloadDirectory))
            {
                logger?.LogInformation("FFmpeg was not found. Video thumbnails are disabled.");
                return null;
            }

            string downloadDir = Path.GetFullPath(options.DownloadDirectory);
            Directory.CreateDirectory(downloadDir);
            resolved = Probe(downloadDir, options.RequireFfprobe);
            if (resolved is not null)
            {
                TryMarkExecutable(resolved, logger);
                Use(resolved);
                logger?.LogInformation("FFmpeg tools directory: {Path}", resolved.Directory);
                return resolved;
            }

            logger?.LogInformation("Downloading FFmpeg (official build) to {Path}", downloadDir);
            await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, downloadDir, progress: null)
                .WaitAsync(cancellationToken);

            resolved = Probe(downloadDir, options.RequireFfprobe);
            if (resolved is null)
            {
                logger?.LogError("FFmpeg download finished but the binaries were not found under {Path}", downloadDir);
                return null;
            }

            TryMarkExecutable(resolved, logger);
            Use(resolved);
            logger?.LogInformation("FFmpeg tools directory: {Path}", resolved.Directory);
            return resolved;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "FFmpeg is not available. Video thumbnails are disabled.");
            return null;
        }
    }

    public static FfmpegLocation? Resolve(FfmpegBootstrapOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        foreach (string candidate in Candidates(options))
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            FfmpegLocation? found = Probe(candidate, options.RequireFfprobe);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    public static void Use(FfmpegLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        FFmpeg.SetExecutablesPath(location.Directory);
        isAvailable = true;
    }

    public static FfmpegLocation? Probe(string? path, bool requireFfprobe)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string full = Path.GetFullPath(path.Trim());
        string directory = File.Exists(full)
            ? Path.GetDirectoryName(full) ?? full
            : full;

        if (!Directory.Exists(directory))
        {
            return null;
        }

        bool windows = OperatingSystem.IsWindows();
        string ffmpeg = Path.Combine(directory, windows ? "ffmpeg.exe" : "ffmpeg");
        string ffprobe = Path.Combine(directory, windows ? "ffprobe.exe" : "ffprobe");
        bool hasFfmpeg = File.Exists(ffmpeg);
        bool hasFfprobe = File.Exists(ffprobe);

        return !hasFfmpeg || (requireFfprobe && !hasFfprobe)
            ? null
            : new FfmpegLocation(directory, ffmpeg, hasFfprobe ? ffprobe : ffmpeg);
    }

    private static IEnumerable<string> Candidates(FfmpegBootstrapOptions options)
    {
        if (options.PreferLinuxSystemDirectory && OperatingSystem.IsLinux())
        {
            yield return "/usr/bin";
        }

        if (!string.IsNullOrWhiteSpace(options.ConfiguredPath))
        {
            yield return options.ConfiguredPath;
        }

        if (options.SearchWellKnownLocations)
        {
            if (OperatingSystem.IsWindows())
            {
                yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin");
                yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ffmpeg");
            }

            if (OperatingSystem.IsMacOS())
            {
                yield return "/opt/homebrew/bin";
            }

            yield return "/usr/bin";
            yield return "/usr/local/bin";
        }

        if (options.SearchApplicationDirectories)
        {
            yield return AppContext.BaseDirectory;
            yield return Directory.GetCurrentDirectory();
        }

        if (!options.SearchPath)
        {
            yield break;
        }

        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            yield break;
        }

        foreach (string entry in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            yield return entry.Trim();
        }
    }

    /// <summary>
    /// Zip extraction does not keep the executable bit, so a downloaded binary cannot be started
    /// on Linux or macOS until it is set.
    /// </summary>
    private static void TryMarkExecutable(FfmpegLocation location, ILogger? logger)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            Mark(location.FfmpegPath);
            if (!string.Equals(location.FfmpegPath, location.FfprobePath, StringComparison.Ordinal))
            {
                Mark(location.FfprobePath);
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not mark {Path} as executable.", location.FfmpegPath);
        }

        static void Mark(string path)
        {
            UnixFileMode mode = File.GetUnixFileMode(path);
            File.SetUnixFileMode(path, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }
    }
}
