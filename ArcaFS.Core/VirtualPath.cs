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
    /// <param name="path">
    /// Caller-supplied path. <see langword="null"/>, empty, or only dots and slashes becomes <c>/</c>.
    /// </param>
    /// <returns>The canonical virtual path.</returns>
    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Constants.RootPath;
        }

        var segments = new List<string>();
        foreach (string span in path.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
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
    /// <param name="normalizedPath">A path already passed through <see cref="Normalize"/>.</param>
    /// <returns>The segment count.</returns>
    public static int Depth(string normalizedPath) =>
        normalizedPath == Constants.RootPath
            ? 0
            : normalizedPath.Count(c => c == Constants.PathSeparator);

    /// <summary>
    /// The individual segments of a normalized path, root first. Returns an empty array for the
    /// root. Backs breadcrumb construction and index depth bookkeeping.
    /// </summary>
    /// <param name="normalizedPath">A path already passed through <see cref="Normalize"/>.</param>
    /// <returns>The segments, or an empty array for the root.</returns>
    public static string[] GetSegments(string normalizedPath) =>
        normalizedPath == Constants.RootPath || string.IsNullOrEmpty(normalizedPath)
            ? []
            : normalizedPath.Split(Constants.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Last segment, or an empty string for the root.</summary>
    /// <param name="normalizedPath">A path already passed through <see cref="Normalize"/>.</param>
    /// <returns>The last segment, or an empty string for the root.</returns>
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
    /// <param name="normalizedPath">A path already passed through <see cref="Normalize"/>.</param>
    /// <returns>The parent, or <see langword="null"/> for the root.</returns>
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
    /// <param name="normalizedPath">Path the segments are appended to. It is normalized again.</param>
    /// <param name="segments">Segments to append. Each is normalized with the path, so <c>..</c> still cannot escape the root.</param>
    /// <returns>The combined virtual path.</returns>
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
    /// <param name="candidate">Path being tested.</param>
    /// <param name="ancestor">Path it may equal or sit under.</param>
    /// <returns><see langword="true"/> when <paramref name="candidate"/> is the ancestor or a descendant.</returns>
    public static bool IsSameOrUnder(string candidate, string ancestor) => ancestor == Constants.RootPath || candidate.Equals(ancestor, StringComparison.OrdinalIgnoreCase) || (candidate.Length > ancestor.Length
            && candidate[ancestor.Length] == Constants.PathSeparator
            && candidate.StartsWith(ancestor, StringComparison.OrdinalIgnoreCase));

    /// <summary>Strictly below <paramref name="ancestor"/> — excludes the ancestor itself.</summary>
    /// <param name="candidate">Path being tested.</param>
    /// <param name="ancestor">Path it must sit strictly under.</param>
    /// <returns><see langword="true"/> when <paramref name="candidate"/> is below the ancestor.</returns>
    public static bool IsStrictlyUnder(string candidate, string ancestor) =>
        !candidate.Equals(ancestor, StringComparison.OrdinalIgnoreCase)
        && IsSameOrUnder(candidate, ancestor);

    /// <summary>
    /// Re-bases <paramref name="candidate"/> so it becomes relative to <paramref name="scope"/>.
    /// Returns <c>null</c> when it isn't inside the scope. Used to hide a user's scope prefix
    /// from the paths their browser ever sees.
    /// </summary>
    /// <param name="candidate">Absolute path within the source.</param>
    /// <param name="scope">The caller's scope prefix, or <c>/</c> when they are unscoped.</param>
    /// <returns>The path with the scope removed, or <see langword="null"/> when it is outside the scope.</returns>
    public static string? MakeRelativeTo(string candidate, string scope) => !IsSameOrUnder(candidate, scope)
            ? null
            : scope == Constants.RootPath
            ? candidate
            : candidate.Length == scope.Length
            ? Constants.RootPath
            : Normalize(candidate[scope.Length..]);

    /// <summary>
    /// Expands <c>${username}</c> / <c>${userid}</c> in a rule path or default scope. Tokens are
    /// slug-sanitised so a display name containing a slash can't widen the rule's reach.
    /// </summary>
    /// <param name="path">Rule path or scope that may contain tokens.</param>
    /// <param name="userName">Replacement for <c>${username}</c>. Sanitized to one segment.</param>
    /// <param name="userId">Replacement for <c>${userid}</c>. Sanitized to one segment.</param>
    /// <returns>The path with both tokens replaced. A path with no token is returned unchanged.</returns>
    public static string ExpandTokens(string path, string? userName, string? userId) => string.IsNullOrEmpty(path) || !path.Contains("${", StringComparison.Ordinal)
            ? path
            : path
            .Replace(Constants.PathTokens.UserName, SanitizeSegment(userName), StringComparison.OrdinalIgnoreCase)
            .Replace(Constants.PathTokens.UserId, SanitizeSegment(userId), StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the path still contains an unexpanded token.</summary>
    /// <param name="path">Path to inspect. <see langword="null"/> or empty is <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the text contains <c>${</c>.</returns>
    public static bool HasTokens(string? path) =>
        !string.IsNullOrEmpty(path) && path.Contains("${", StringComparison.Ordinal);

    /// <summary>
    /// Reduces an arbitrary string to something usable as a single path segment: separators and
    /// characters illegal on Windows are replaced with <c>_</c>.
    /// </summary>
    /// <param name="value">Text to turn into one segment. Blank becomes <c>_</c>.</param>
    /// <returns>The sanitized segment. A result that is empty after trimming also becomes <c>_</c>.</returns>
    public static string SanitizeSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "_";
        }

        char[] buffer = new char[value.Length];
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
    /// <param name="sourceRoot">Physical root of the source.</param>
    /// <param name="virtualPath">Virtual path to map. It is normalized first.</param>
    /// <param name="physicalPath">The physical path when the result is inside the root.</param>
    /// <returns>
    /// <see langword="false"/> when the root or the combined path cannot be resolved, or when the
    /// result is outside the root. Linux compares the containment check ordinally; other systems
    /// ignore case.
    /// </returns>
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
    /// <param name="sourceRoot">Physical root of the source.</param>
    /// <param name="physicalPath">Physical path under that root.</param>
    /// <returns>
    /// The virtual path. A physical path that is not longer than the root becomes <c>/</c>.
    /// </returns>
    public static string FromPhysical(string sourceRoot, string physicalPath)
    {
        string rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceRoot));
        string full = Path.GetFullPath(physicalPath);

        return full.Length <= rootFull.Length
            ? Constants.RootPath
            : Normalize(full[rootFull.Length..].Replace(Path.DirectorySeparatorChar, Constants.PathSeparator));
    }
}