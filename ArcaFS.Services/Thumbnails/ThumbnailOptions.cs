namespace ArcaFS.Services.Thumbnails;

/// <summary>
/// Bound from the <c>Thumbnails</c> configuration section.
/// </summary>
public sealed class ThumbnailOptions
{
    public const string SectionName = "Thumbnails";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Where generated thumbnails are stored. Kept outside any source so cache files can never
    /// appear in a user's listing. Defaults under the app's data directory.
    /// </summary>
    public string CacheDirectory { get; set; } = string.Empty;

    /// <summary>WebP quality, 1-100. 75 is visually indistinguishable at thumbnail scale.</summary>
    public int Quality { get; set; } = 75;

    /// <summary>Cached files untouched for this long are removed by the nightly prune job.</summary>
    public int CacheRetentionDays { get; set; } = 30;
}

/// <summary>Generated thumbnail bytes plus what the response needs to serve them.</summary>
public sealed record ThumbnailDto(byte[] Bytes, string MimeType, int Size);
