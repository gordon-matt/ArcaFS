using Heroicsoft.NetForge.Drawing;

namespace ArcaFS.Services.FileSystem;

/// <summary>
/// Reads image dimensions without decoding pixel data. Identification parses only the header,
/// so this stays cheap even for a large photo and never allocates a full frame buffer.
/// </summary>
internal static class ImageProbe
{
    public static async Task<(int? Width, int? Height)> TryGetDimensionsAsync(
        string physicalPath,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var info = await new ImageProcessor().TryIdentifyAsync(physicalPath, cancellationToken);
            return (info?.Width, info?.Height);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A file with an image extension that isn't a valid image is a normal thing to find
            // on a filesystem; the info panel just omits the dimensions.
            logger.LogDebug(ex, "Could not read image dimensions from {Path}", physicalPath);
            return (null, null);
        }
    }
}