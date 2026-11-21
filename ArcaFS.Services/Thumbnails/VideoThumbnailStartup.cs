using ArcaFS.Services.Thumbnails.Ffmpeg;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ArcaFS.Services.Thumbnails;

/// <summary>
/// Points Xabe at ffmpeg before the app accepts requests. Failure leaves video thumbnails off.
/// </summary>
internal sealed class VideoThumbnailStartup(
    IOptions<VideoThumbnailOptions> options,
    IHostEnvironment environment,
    VideoThumbnailGate gate,
    ILogger<VideoThumbnailStartup> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        VideoThumbnailOptions settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("Video thumbnails are disabled.");
            return;
        }

        string? downloadDirectory = settings.DownloadDirectory;
        if (settings.DownloadIfMissing && string.IsNullOrWhiteSpace(downloadDirectory))
        {
            downloadDirectory = Path.Combine(environment.ContentRootPath, "ffmpeg");
        }

        FfmpegLocation? location = await FfmpegBootstrap.EnsureAsync(
            new FfmpegBootstrapOptions
            {
                ConfiguredPath = settings.FfmpegPath,
                DownloadDirectory = downloadDirectory,
                SearchWellKnownLocations = settings.SearchWellKnownLocations,
                SearchApplicationDirectories = settings.SearchApplicationDirectories,
                SearchPath = settings.SearchPath,
                PreferLinuxSystemDirectory = settings.PreferLinuxSystemDirectory,
                DownloadIfMissing = settings.DownloadIfMissing,
                RequireFfprobe = true,
            },
            logger,
            cancellationToken);

        gate.IsAvailable = location is not null;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Set by <see cref="VideoThumbnailStartup"/> once ffmpeg has been located.</summary>
public sealed class VideoThumbnailGate : IVideoThumbnailGate
{
    public bool IsAvailable { get; set; }
}
