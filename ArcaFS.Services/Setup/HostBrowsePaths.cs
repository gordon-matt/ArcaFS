namespace ArcaFS.Services.Setup;

/// <summary>
/// Keeps the in-app folder picker inside the configured data directory. A container process
/// can otherwise open <c>/</c> and read the image's filesystem.
/// </summary>
internal static class HostBrowsePaths
{
    public const string DefaultDataRoot = "/app/data";

    /// <summary>
    /// The jail applies only when the configured directory exists, so a dev machine that has
    /// no <c>/app/data</c> can still pick a local folder.
    /// </summary>
    public static bool TryGetEnforcedRoot(string? configuredRoot, out string root)
    {
        root = string.Empty;

        if (string.IsNullOrWhiteSpace(configuredRoot) || !TryNormalize(configuredRoot, out string full))
        {
            return false;
        }

        if (!Directory.Exists(full))
        {
            return false;
        }

        root = full;
        return true;
    }

    public static bool IsInside(string candidate, string root)
    {
        if (!TryNormalize(candidate, out string full) || !TryNormalize(root, out string normalizedRoot))
        {
            return false;
        }

        if (PathsEqual(full, normalizedRoot) || IsFilesystemRoot(normalizedRoot))
        {
            return true;
        }

        string prefix = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;

        return full.StartsWith(prefix, Comparison);
    }

    /// <summary>
    /// A symlink whose final target leaves the data directory would list that target while the
    /// path string still looked safe. The configured root itself is allowed to be a symlink,
    /// because that is how some hosts mount the data volume.
    /// </summary>
    public static bool EscapesViaSymlink(string path, string root)
    {
        if (!TryNormalize(path, out string full) || !TryNormalize(root, out string normalizedRoot))
        {
            return true;
        }

        if (PathsEqual(full, normalizedRoot) || !Directory.Exists(full))
        {
            return false;
        }

        try
        {
            FileSystemInfo? target = new DirectoryInfo(full).ResolveLinkTarget(returnFinalTarget: true);
            return target is not null && !IsInside(target.FullName, normalizedRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    public static bool IsAllowed(string path, string root) =>
        IsInside(path, root) && !EscapesViaSymlink(path, root);

    /// <summary>
    /// Parent directory for the picker's up button, or null when that would leave the jail.
    /// </summary>
    public static string? ParentWithinRoot(string current, string root)
    {
        if (!TryNormalize(current, out string full) || !IsInside(full, root))
        {
            return null;
        }

        if (!TryNormalize(root, out string normalizedRoot) || PathsEqual(full, normalizedRoot))
        {
            return null;
        }

        string? parent = Path.GetDirectoryName(full);
        if (string.IsNullOrEmpty(parent) || !IsInside(parent, normalizedRoot))
        {
            return null;
        }

        return parent;
    }

    private static bool TryNormalize(string path, out string normalized)
    {
        normalized = string.Empty;

        try
        {
            string full = Path.GetFullPath(path);
            normalized = TrimTrailingSeparator(full);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string TrimTrailingSeparator(string path)
    {
        if (path.Length <= 1 || IsFilesystemRoot(path))
        {
            return path;
        }

        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsFilesystemRoot(string path) =>
        path is "/" or "\\"
        || (path.Length == 3 && path[1] == ':' && path[2] is '\\' or '/');

    private static bool PathsEqual(string left, string right) =>
        string.Equals(left, right, Comparison);

    private static StringComparison Comparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
