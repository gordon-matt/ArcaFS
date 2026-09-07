using System.Text.RegularExpressions;

namespace ArcaFS.Services.FileSystem;

/// <summary>
/// Matches entry names against a source's newline-separated exclude globs. Compiled once per
/// source and cached, because a listing tests every entry against every pattern.
/// </summary>
public sealed partial class ExcludeMatcher
{
    private static readonly ExcludeMatcher Empty = new([]);

    private static readonly Dictionary<int, (string? Patterns, ExcludeMatcher Matcher)> Cache = [];
    private static readonly Lock CacheLock = new();

    private readonly Regex[] patterns;

    private ExcludeMatcher(Regex[] patterns) => this.patterns = patterns;

    /// <summary>
    /// Returns the matcher for <paramref name="source"/>, rebuilding it if the source's patterns
    /// have changed since it was cached.
    /// </summary>
    public static ExcludeMatcher For(Source source)
    {
        if (string.IsNullOrWhiteSpace(source.ExcludePatterns))
        {
            return Empty;
        }

        lock (CacheLock)
        {
            if (Cache.TryGetValue(source.Id, out var cached)
                && string.Equals(cached.Patterns, source.ExcludePatterns, StringComparison.Ordinal))
            {
                return cached.Matcher;
            }

            var matcher = Build(source.ExcludePatterns);
            Cache[source.Id] = (source.ExcludePatterns, matcher);

            return matcher;
        }
    }

    public bool IsExcluded(string name) =>
        patterns.Length != 0 && patterns.Any(p => p.IsMatch(name));

    private static ExcludeMatcher Build(string patternText)
    {
        var regexes = patternText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !p.StartsWith('#'))
            .Select(TryCompile)
            .Where(r => r is not null)
            .Select(r => r!)
            .ToArray();

        return regexes.Length == 0 ? Empty : new ExcludeMatcher(regexes);
    }

    private static Regex? TryCompile(string glob)
    {
        try
        {
            return new Regex(GlobToRegex(glob), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        catch (ArgumentException)
        {
            // A malformed pattern shouldn't break browsing; it just excludes nothing.
            return null;
        }
    }

    /// <summary>
    /// Translates a shell glob to an anchored regex. Only <c>*</c> and <c>?</c> are meaningful;
    /// everything else is escaped, so a pattern like <c>node_modules</c> matches literally and a
    /// user can't accidentally write a catastrophically backtracking expression.
    /// </summary>
    private static string GlobToRegex(string glob)
    {
        var builder = new System.Text.StringBuilder("^");

        foreach (char c in glob)
        {
            builder.Append(c switch
            {
                '*' => "[^/]*",
                '?' => "[^/]",
                _ => Regex.Escape(c.ToString()),
            });
        }

        return builder.Append('$').ToString();
    }
}
