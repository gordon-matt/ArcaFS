using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace ArcaFS.Services.Thumbnails;

public sealed class ThumbnailService(
    ILogger<ThumbnailService> logger,
    IPermissionService permissionService,
    IOptions<ThumbnailOptions> options) : IThumbnailService
{
    /// <summary>
    /// Sizes the UI is allowed to ask for. Unbounded sizes would let one request fill the cache
    /// directory with near-identical files, and would defeat caching entirely.
    /// </summary>
    private static readonly int[] AllowedSizes = [128, 256, 512];

    /// <summary>
    /// Refuses to decode images beyond this. Decoding is proportional to pixel count, not file
    /// size, so a small but enormous-dimensioned file is the risk (the "decompression bomb").
    /// </summary>
    private const int MaxPixels = 100_000_000;

    private readonly ThumbnailOptions settings = options.Value;

    /// <summary>
    /// Serialises generation per cache key so a grid of forty tiles requesting the same
    /// thumbnail at once produces one decode rather than forty.
    /// </summary>
    private static readonly SemaphoreSlim GenerationLock = new(Environment.ProcessorCount);

    public async Task<Result<ThumbnailDto>> GetAsync(
        int sourceId,
        string path,
        int size = 256,
        AccessPrincipal? principal = null,
        CancellationToken cancellationToken = default)
    {
        if (!settings.Enabled)
        {
            return Result<ThumbnailDto>.NotFound();
        }

        // Snap to the nearest allowed size rather than rejecting, so a caller asking for 200
        // gets a usable image instead of an error.
        int resolvedSize = AllowedSizes.MinBy(s => Math.Abs(s - size));

        var required = FilePermission.Read;

        var resolved = principal is null
            ? await permissionService.RequireAsync(sourceId, path, required, cancellationToken)
            : await permissionService.RequireAsync(sourceId, path, required, principal, cancellationToken);

        if (!resolved.IsSuccess)
        {
            return resolved.To<ThumbnailDto>();
        }

        var target = resolved.Value;

        if (!File.Exists(target.PhysicalPath))
        {
            return Result<ThumbnailDto>.NotFound();
        }

        if (FileKinds.Classify(Path.GetFileName(target.PhysicalPath), false) != FileKind.Image)
        {
            return Result<ThumbnailDto>.NotFound();
        }

        var info = new FileInfo(target.PhysicalPath);
        string cachePath = BuildCachePath(sourceId, target.AbsoluteVirtualPath, resolvedSize, info);

        if (File.Exists(cachePath))
        {
            try
            {
                byte[] cached = await File.ReadAllBytesAsync(cachePath, cancellationToken);
                return Result<ThumbnailDto>.Success(new ThumbnailDto(cached, WebpMimeType, resolvedSize));
            }
            catch (IOException ex)
            {
                // A partially written or locked cache file just means we regenerate.
                logger.LogDebug(ex, "Could not read cached thumbnail {Cache}", cachePath);
            }
        }

        await GenerationLock.WaitAsync(cancellationToken);

        try
        {
            byte[] bytes = await GenerateAsync(target.PhysicalPath, resolvedSize, cancellationToken);

            await WriteCacheAsync(cachePath, bytes, cancellationToken);

            return Result<ThumbnailDto>.Success(new ThumbnailDto(bytes, WebpMimeType, resolvedSize));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Files with an image extension that aren't decodable images are common. The UI
            // falls back to an icon, so this is not worth an error-level log.
            logger.LogDebug(ex, "Could not generate a thumbnail for {Physical}", target.PhysicalPath);

            return Result<ThumbnailDto>.NotFound();
        }
        finally
        {
            GenerationLock.Release();
        }
    }

    public Task<int> PruneAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(settings.CacheDirectory))
        {
            return Task.FromResult(0);
        }

        int removed = 0;
        var cutoff = DateTime.UtcNow.AddDays(-settings.CacheRetentionDays);

        try
        {
            // The cache key contains the source file's size and timestamp, so a stale entry is
            // simply never requested again. Pruning by last access is therefore sufficient and
            // avoids having to map every cache file back to its original.
            foreach (var file in new DirectoryInfo(settings.CacheDirectory)
                .EnumerateFiles("*.webp", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (file.LastAccessTimeUtc >= cutoff)
                {
                    continue;
                }

                try
                {
                    file.Delete();
                    removed++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogDebug(ex, "Could not prune thumbnail {Path}", file.FullName);
                }
            }

            if (removed > 0)
            {
                logger.LogInformation("Pruned {Count} cached thumbnail(s)", removed);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not prune the thumbnail cache");
        }

        return Task.FromResult(removed);
    }

    private const string WebpMimeType = "image/webp";

    private async Task<byte[]> GenerateAsync(
        string physicalPath,
        int size,
        CancellationToken cancellationToken)
    {
        var decoderOptions = new DecoderOptions
        {
            // Ask the decoder for something close to the target size. JPEG can honour this
            // during decode, which avoids materialising a 60-megapixel bitmap to produce a
            // 256-pixel square.
            TargetSize = new Size(size, size),
        };

        using var image = await Image.LoadAsync(decoderOptions, physicalPath, cancellationToken);

        if ((long)image.Width * image.Height > MaxPixels)
        {
            throw new InvalidOperationException(
                $"{physicalPath} is {image.Width}x{image.Height}, which exceeds the decode limit.");
        }

        image.Mutate(context => context.Resize(new ResizeOptions
        {
            Size = new Size(size, size),

            // Max preserves aspect ratio and never upscales, so a small icon stays crisp
            // instead of being blown up and blurred.
            Mode = ResizeMode.Max,
            Sampler = KnownResamplers.Lanczos3,
        }));

        // Strip metadata: EXIF can carry GPS coordinates, and a thumbnail served to a share
        // visitor should not leak where a photo was taken.
        image.Metadata.ExifProfile = null;
        image.Metadata.IptcProfile = null;
        image.Metadata.XmpProfile = null;

        using var output = new MemoryStream();

        await image.SaveAsync(
            output,
            new WebpEncoder { Quality = settings.Quality },
            cancellationToken);

        return output.ToArray();
    }

    private async Task WriteCacheAsync(string cachePath, byte[] bytes, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

            // Written to a temp name and moved, so a concurrent reader never sees a half-written
            // file. The move is atomic on both NTFS and ext4.
            string temporary = cachePath + ".tmp";
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
            File.Move(temporary, cachePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Caching is an optimisation. Failing to cache must not fail the request.
            logger.LogDebug(ex, "Could not cache thumbnail at {Cache}", cachePath);
        }
    }

    /// <summary>
    /// Builds a cache path from the source, virtual path, requested size and the file's own size
    /// and timestamp. Including the last two means editing a file invalidates its thumbnail
    /// automatically, with no cache-busting bookkeeping anywhere else.
    /// </summary>
    private string BuildCachePath(int sourceId, string virtualPath, int size, FileInfo info)
    {
        string key = $"{sourceId}|{virtualPath}|{size}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        string hex = Convert.ToHexStringLower(hash);

        // Two levels of fan-out keeps directory sizes reasonable; a single flat folder with
        // hundreds of thousands of entries is slow to enumerate on most filesystems.
        return Path.Combine(settings.CacheDirectory, hex[..2], hex[2..4], $"{hex}.webp");
    }
}