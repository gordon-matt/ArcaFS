using ArcaFS.Services.Thumbnails.Ffmpeg;
using Xabe.FFmpeg;

namespace ArcaFS.Services.Thumbnails;

/// <summary>
/// Grabs one JPEG from the middle of a video so the normal thumbnail pipeline can scale it.
/// Returns null when ffmpeg is missing or the file has no readable frame.
/// </summary>
public sealed class VideoFrameCapture(ILogger<VideoFrameCapture> logger, IVideoThumbnailGate gate)
{
    private const int MaxConcurrentCaptures = 2;
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(15);
    private static readonly SemaphoreSlim Slot = new(MaxConcurrentCaptures, MaxConcurrentCaptures);

    public async Task<byte[]?> TryCaptureAsync(string videoPath, CancellationToken cancellationToken)
    {
        if (!gate.IsAvailable || !FfmpegBootstrap.IsAvailable || !File.Exists(videoPath))
        {
            return null;
        }

        string framePath = Path.Combine(Path.GetTempPath(), "arcafs-" + Guid.NewGuid().ToString("N") + ".jpg");
        await Slot.WaitAsync(cancellationToken);
        try
        {
            TimeSpan at = await GetMidpointAsync(videoPath, cancellationToken);
            bool captured = await TryCaptureAsync(videoPath, framePath, at, cancellationToken)
                || (at > TimeSpan.Zero && await TryCaptureAsync(videoPath, framePath, TimeSpan.Zero, cancellationToken));
            if (!captured)
            {
                return null;
            }

            return await File.ReadAllBytesAsync(framePath, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not read a frame from {Path}", videoPath);
            return null;
        }
        finally
        {
            Slot.Release();
            TryDelete(framePath);
        }
    }

    private static async Task<TimeSpan> GetMidpointAsync(string videoPath, CancellationToken cancellationToken)
    {
        IMediaInfo media = await FFmpeg.GetMediaInfo(videoPath, cancellationToken);
        TimeSpan duration = media.Duration;
        if (duration <= TimeSpan.Zero)
        {
            duration = media.VideoStreams.FirstOrDefault()?.Duration ?? TimeSpan.Zero;
        }

        return duration > TimeSpan.Zero
            ? TimeSpan.FromTicks(duration.Ticks / 2)
            : TimeSpan.Zero;
    }

    private static async Task<bool> TryCaptureAsync(
        string videoPath,
        string outputPath,
        TimeSpan at,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CaptureTimeout);
        try
        {
            IConversion conversion = await FFmpeg.Conversions.FromSnippet.Snapshot(videoPath, outputPath, at);
            conversion.SetOverwriteOutput(true);
            // A crafted file can otherwise make ffmpeg open http, concat, or subfile targets.
            conversion.AddParameter("-nostdin -protocol_whitelist file", ParameterPosition.PreInput);
            await conversion.Start(timeout.Token);
            return File.Exists(outputPath) && new FileInfo(outputPath).Length > 0;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }
}
