namespace ArcaFS.Services.Uploads;

/// <summary>
/// Shrinks uploaded images that exceed a maximum size. Off unless a host turns it on: a file
/// server should store the bytes it was given until someone asks it not to.
/// </summary>
public sealed class ImageResizeOptions
{
    public const string SectionName = "Uploads:ImageResize";

    public bool Enabled { get; set; }

    /// <summary>Zero means no limit on this axis.</summary>
    public int MaxWidth { get; set; } = 2560;

    /// <summary>Zero means no limit on this axis.</summary>
    public int MaxHeight { get; set; } = 2560;
}
