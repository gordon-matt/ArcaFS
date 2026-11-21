using Heroicsoft.NetForge.Drawing;
using Microsoft.Extensions.Options;

namespace ArcaFS.Services.Uploads;

/// <summary>Shrinks an uploaded image in place when it is larger than <see cref="ImageResizeOptions"/>.</summary>
public sealed class ImageResizer(
    ILogger<ImageResizer> logger,
    IImageProcessor images,
    IOptionsMonitor<ImageResizeOptions> options)
{
    /// <summary>
    /// Refuses to decode images beyond this many pixels. Decoding cost follows pixel count, not
    /// file size, so a small file with enormous dimensions is the risk.
    /// </summary>
    internal const long MaxPixels = 100_000_000;

    private const int ShrinkQuality = 75;

    public async Task TryShrinkAsync(string physicalPath, string fileName, CancellationToken cancellationToken)
    {
        ImageResizeOptions settings = options.CurrentValue;
        if (!settings.Enabled
            || (settings.MaxWidth <= 0 && settings.MaxHeight <= 0)
            || !File.Exists(physicalPath)
            || !CanShrink(fileName))
        {
            return;
        }

        try
        {
            RasterInfo? info = await images.TryIdentifyAsync(physicalPath, cancellationToken);
            if (info is null || !NeedsShrink(info.Width, info.Height, settings.MaxWidth, settings.MaxHeight))
            {
                return;
            }

            if ((long)info.Width * info.Height > MaxPixels)
            {
                logger.LogWarning(
                    "Skipped shrinking {Path}: {Width}x{Height} exceeds the pixel limit",
                    physicalPath,
                    info.Width,
                    info.Height);
                return;
            }

            ProcessedImage processed = await images.ResizeAsync(physicalPath, new ImageTransform
            {
                Width = settings.MaxWidth > 0 ? settings.MaxWidth : info.Width,
                Height = settings.MaxHeight > 0 ? settings.MaxHeight : info.Height,
                Mode = ImageScaleMode.Max,
                Quality = ShrinkQuality,
                MaxPixels = MaxPixels,
            }, cancellationToken);

            string temporary = physicalPath + ".resize.tmp";
            await File.WriteAllBytesAsync(temporary, processed.Bytes, cancellationToken);
            File.Move(temporary, physicalPath, overwrite: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Could not shrink uploaded image {Path}", physicalPath);
        }
    }

    internal static bool CanShrink(string fileName)
    {
        if (FileKinds.Classify(fileName, isDirectory: false) != FileKind.Image)
        {
            return false;
        }

        // SVG is text. Resizing it would rasterise a file the user expects to stay a drawing.
        return !string.Equals(Path.GetExtension(fileName), ".svg", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool NeedsShrink(int width, int height, int maxWidth, int maxHeight) =>
        (maxWidth > 0 && width > maxWidth) || (maxHeight > 0 && height > maxHeight);
}
