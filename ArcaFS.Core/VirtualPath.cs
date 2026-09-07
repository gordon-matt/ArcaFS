using System.Diagnostics.CodeAnalysis;

namespace ArcaFS;

/// <summary>
/// Normalisation and containment helpers for *virtual* paths — the source-relative, always
/// forward-slashed paths that appear in URLs, access rules and the index.
/// <para>
/// Every path that arrives from a browser must pass through <see cref="Normalize"/> before it
/// touches the filesystem, and every physical path must be produced by <see cref="TryResolve"/>
/// so a <c>..</c> segment or a rooted path can't escape its source directory.
/// </para>
/// </summary>
public static class VirtualPath
{
    private static readonly char[] Separators = ['/', '\\'];

    /// <summary>
    /// Canonicalises a caller-supplied path to the form <c>/</c> or <c>/a/b/c</c>: forward
    /// slashes, single-separated, no trailing slash, with <c>.</c> and <c>..</c> segments
    /// resolved. <c>..</c> can never climb above the root — it is discarded at depth zero, so
    /// the result is always inside the source.
    /// </summary>
    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Constants.RootPath;
        }

        var segments = new List<string>();
        foreach (var span in path.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            string segment = span.Trim();
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }

                // At the root there is nothing to pop; swallowing it keeps callers inside the source.
                continue;
            }

            segments.Add(segment);
        }

        return segments.Count == 0
            ? Constants.RootPath
            : Constants.PathSeparator + string.Join(Constants.PathSeparator, segments);
    }

    /// <summary>Number of segments below the root. <c>/</c> is 0, <c>/a/b</c> is 2.</summary>
    public static int Depth(string normalizedPath) =>
        normalizedPath == Constants.RootPath
            ? 0
            : normalizedPath.Count(c => c == Constants.PathSeparator);

    /// <summary>
    /// The individual segments of a normalized path, root first. Returns an empty array for the
    /// root. Backs breadcrumb construction and index depth bookkeeping.
    /// </summary>
    public static string[] GetSegments(string normalizedPath) =>
        normalizedPath == Constants.RootPath || string.IsNullOrEmpty(normalizedPath)
            ? []
            : normalizedPath.Split(Constants.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Last segment, or an empty string for the root.</summary>
    public static string GetName(string normalizedPath)
    {
        if (normalizedPath == Constants.RootPath)
        {
            return string.Empty;
        }

        int index = normalizedPath.LastIndexOf(Constants.PathSeparator);
        return index < 0 ? normalizedPath : normalizedPath[(index + 1)..];
    }

    /// <summary>Parent path, or <c>null</c> when <paramref name="normalizedPath"/> is the root.</summary>
    public static string? GetParent(string normalizedPath)
    {
        if (normalizedPath == Constants.RootPath)
        {
            return null;
        }

        int index = normalizedPath.LastIndexOf(Constants.PathSeparator);
        return index <= 0 ? Constants.RootPath : normalizedPath[..index];
    }

    /// <summary>Appends one or more segments to a normalized path.</summary>
    public static string Combine(string normalizedPath, params string[] segments)
    {
        if (segments.Length == 0)
        {
            return Normalize(normalizedPath);
        }

        string tail = string.Join(Constants.PathSeparator, segments);
        return normalizedPath == Constants.RootPath
            ? Normalize(Constants.PathSeparator + tail)
            : Normalize(normalizedPath + Constants.PathSeparator + tail);
    }

    /// <summary>
    /// True when <paramref name="candidate"/> is <paramref name="ancestor"/> itself or sits
    /// underneath it. Compares whole segments, so <c>/ab</c> is not treated as a child of
    /// <c>/a</c>. Case-insensitive to match Windows and macOS filesystem behaviour.
    /// </summary>
    public static bool IsSameOrUnder(string candidate, string ancestor)
    {
        if (ancestor == Constants.RootPath)
        {
            return true;
        }

        if (candidate.Equals(ancestor, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return candidate.Length > ancestor.Length
            && candidate[ancestor.Length] == Constants.PathSeparator
            && candidate.StartsWith(ancestor, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Strictly below <paramref name="ancestor"/> — excludes the ancestor itself.</summary>
    public static bool IsStrictlyUnder(string candidate, string ancestor) =>
        !candidate.Equals(ancestor, StringComparison.OrdinalIgnoreCase)
        && IsSameOrUnder(candidate, ancestor);

    /// <summary>
    /// Re-bases <paramref name="candidate"/> so it becomes relative to <paramref name="scope"/>.
    /// Returns <c>null</c> when it isn't inside the scope. Used to hide a user's scope prefix
    /// from the paths their browser ever sees.
    /// </summary>
    public static string? MakeRelativeTo(string candidate, string scope)
    {
        if (!IsSameOrUnder(candidate, scope))
        {
            return null;
        }

        if (scope == Constants.RootPath)
        {
            return candidate;
        }

        return candidate.Length == scope.Length
            ? Constants.RootPath
            : Normalize(candidate[scope.Length..]);
    }

    /// <summary>
    /// Expands <c>${username}</c> / <c>${userid}</c> in a rule path or default scope. Tokens are
    /// slug-sanitised so a display name containing a slash can't widen the rule's reach.
    /// </summary>
    public static string ExpandTokens(string path, string? userName, string? userId)
    {
        if (string.IsNullOrEmpty(path) || !path.Contains("${", StringComparison.Ordinal))
        {
            return path;
        }

        return path
            .Replace(Constants.PathTokens.UserName, SanitizeSegment(userName), StringComparison.OrdinalIgnoreCase)
            .Replace(Constants.PathTokens.UserId, SanitizeSegment(userId), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when the path still contains an unexpanded token.</summary>
    public static bool HasTokens(string? path) =>
        !string.IsNullOrEmpty(path) && path.Contains("${", StringComparison.Ordinal);

    /// <summary>
    /// Reduces an arbitrary string to something usable as a single path segment: separators and
    /// characters illegal on Windows are replaced with <c>_</c>.
    /// </summary>
    public static string SanitizeSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "_";
        }

        var buffer = new char[value.Length];
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            buffer[i] = c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' || char.IsControl(c)
                ? '_'
                : c;
        }

        string result = new string(buffer).Trim().TrimEnd('.');
        return result.Length == 0 ? "_" : result;
    }

    /// <summary>
    /// Maps a virtual path onto a physical one under <paramref name="sourceRoot"/> and verifies
    /// the result really is inside that root after the OS has resolved it. This is the last line
    /// of defence against traversal: a symlink or a normalisation quirk that escapes the root
    /// fails here rather than being served.
    /// </summary>
    public static bool TryResolve(string sourceRoot, string virtualPath, [NotNullWhen(true)] out string? physicalPath)
    {
        physicalPath = null;

        string normalized = Normalize(virtualPath);
        string rootFull;
        try
        {
            rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceRoot));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        string relative = normalized == Constants.RootPath
            ? string.Empty
            : normalized[1..].Replace(Constants.PathSeparator, Path.DirectorySeparatorChar);

        string combined;
        try
        {
            combined = Path.GetFullPath(Path.Combine(rootFull, relative));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        string comparable = Path.TrimEndingDirectorySeparator(combined);
        var comparison = OperatingSystem.IsLinux()
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        bool contained = comparable.Equals(rootFull, comparison)
            || comparable.StartsWith(rootFull + Path.DirectorySeparatorChar, comparison);

        if (!contained)
        {
            return false;
        }

        physicalPath = combined;
        return true;
    }

    /// <summary>Converts a physical path under <paramref name="sourceRoot"/> back to a virtual one.</summary>
    public static string FromPhysical(string sourceRoot, string physicalPath)
    {
        string rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceRoot));
        string full = Path.GetFullPath(physicalPath);

        if (full.Length <= rootFull.Length)
        {
            return Constants.RootPath;
        }

        return Normalize(full[rootFull.Length..].Replace(Path.DirectorySeparatorChar, Constants.PathSeparator));
    }
}
