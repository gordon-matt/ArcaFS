namespace ArcaFS;

/// <summary>
/// Maps a per-user home scope onto a folder that already exists on disk.
/// </summary>
/// <remarks>
/// <c>${username}</c> expands to the display name when one is set, otherwise the login name.
/// Operators also often create the folder with different casing. Prefer an existing match over
/// creating a second empty home beside it.
/// </remarks>
public static class UserHomePath
{
    /// <summary>
    /// If <paramref name="sourceRoot"/> already contains a directory matching the expanded
    /// scope, the login name, or the display name, returns a scope that uses that directory's
    /// real casing. Otherwise returns <paramref name="expandedScope"/> unchanged.
    /// </summary>
    public static string AlignScopeWithExistingDirectory(
        string sourceRoot,
        string expandedScope,
        string? userName,
        string? displayName)
    {
        if (expandedScope == Constants.RootPath || string.IsNullOrWhiteSpace(sourceRoot))
        {
            return expandedScope;
        }

        if (!Directory.Exists(sourceRoot))
        {
            return expandedScope;
        }

        string[] names;
        try
        {
            names = Directory.GetDirectories(sourceRoot)
                .Select(Path.GetFileName)
                .Where(static n => !string.IsNullOrEmpty(n))
                .Cast<string>()
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return expandedScope;
        }

        string? match = FirstExisting(names, VirtualPath.GetName(expandedScope))
            ?? FirstExisting(names, VirtualPath.SanitizeSegment(userName))
            ?? FirstExisting(names, displayName is { Length: > 0 } ? VirtualPath.SanitizeSegment(displayName) : null);

        return match is null
            ? expandedScope
            : Constants.PathSeparator + match;
    }

    private static string? FirstExisting(IReadOnlyList<string> names, string? wanted) => string.IsNullOrWhiteSpace(wanted) || wanted == "_"
            ? null
            : names.FirstOrDefault(n => n.Equals(wanted, StringComparison.OrdinalIgnoreCase));
}