namespace ArcaFS.Services.Setup;

/// <summary>
/// Bound from the <c>Hosting</c> configuration section.
/// </summary>
public sealed class HostBrowseOptions
{
    public const string SectionName = "Hosting";

    /// <summary>
    /// Directory the folder picker and new sources are confined to. Docker mounts user data
    /// at <c>/app/data</c>; without this limit the picker can walk the rest of the container.
    /// When the directory does not exist (a local Windows dev host), the picker is not jailed.
    /// </summary>
    public string DataRoot { get; set; } = HostBrowsePaths.DefaultDataRoot;
}
