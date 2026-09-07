using System.Globalization;

namespace ArcaFS.Services.Search;

/// <summary>
/// Parses the search box syntax into a <see cref="SearchQuery"/>.
/// </summary>
/// <remarks>
/// Supported forms:
/// <list type="bullet">
/// <item><c>type:image</c>, <c>type:video</c>, <c>type:folder</c> — restrict by kind</item>
/// <item><c>type:largerThan=50M</c>, <c>type:smallerThan=1G</c> — size bounds</item>
/// <item><c>type:newerThan=2024-01-01</c>, <c>type:olderThan=7d</c> — date bounds</item>
/// <item><c>case:exact</c> — case-sensitive matching</item>
/// <item><c>"annual report"</c> — literal phrase</item>
/// <item><c>draft|final</c> — either term</item>
/// </list>
/// Anything unrecognised is treated as a search term rather than rejected, so a stray colon in
/// a filename still finds the file.
/// </remarks>
public static class SearchQueryParser
{
    public static SearchQuery Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new SearchQuery();
        }

        var terms = new List<string>();
        var kinds = new List<FileKind>();
        bool caseSensitive = false;
        bool exactPhrase = false;
        bool requireAll = true;
        bool? foldersOnly = null;
        long? largerThan = null;
        long? smallerThan = null;
        DateTime? newerThan = null;
        DateTime? olderThan = null;

        foreach (string token in Tokenize(raw))
        {
            if (token.StartsWith("type:", StringComparison.OrdinalIgnoreCase))
            {
                string value = token[5..];

                if (TryParseSizeFilter(value, "largerThan", out long larger))
                {
                    largerThan = larger;
                }
                else if (TryParseSizeFilter(value, "smallerThan", out long smaller))
                {
                    smallerThan = smaller;
                }
                else if (TryParseDateFilter(value, "newerThan", out var newer))
                {
                    newerThan = newer;
                }
                else if (TryParseDateFilter(value, "olderThan", out var older))
                {
                    olderThan = older;
                }
                else if (value.Equals("folder", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("dir", StringComparison.OrdinalIgnoreCase))
                {
                    foldersOnly = true;
                }
                else if (value.Equals("file", StringComparison.OrdinalIgnoreCase))
                {
                    foldersOnly = false;
                }
                else if (TryParseKind(value, out var kind))
                {
                    kinds.Add(kind);
                }

                continue;
            }

            if (token.Equals("case:exact", StringComparison.OrdinalIgnoreCase))
            {
                caseSensitive = true;
                continue;
            }

            if (token.StartsWith('"') && token.EndsWith('"') && token.Length > 2)
            {
                terms.Add(token[1..^1]);
                exactPhrase = true;
                continue;
            }

            // "a|b" means either, which flips the whole query from AND to OR. Simpler than a
            // full boolean tree and covers what people actually type.
            if (token.Contains('|'))
            {
                var alternatives = token
                    .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                if (alternatives.Length > 1)
                {
                    terms.AddRange(alternatives);
                    requireAll = false;
                    continue;
                }
            }

            terms.Add(token);
        }

        return new SearchQuery
        {
            Terms = terms,
            RequireAllTerms = requireAll && !exactPhrase,
            ExactPhrase = exactPhrase,
            CaseSensitive = caseSensitive,
            Kinds = kinds,
            FoldersOnly = foldersOnly,
            LargerThanBytes = largerThan,
            SmallerThanBytes = smallerThan,
            NewerThanUtc = newerThan,
            OlderThanUtc = olderThan,
        };
    }

    /// <summary>
    /// Splits on whitespace while keeping quoted runs together, so <c>"annual report"</c> is one
    /// token rather than two.
    /// </summary>
    private static IEnumerable<string> Tokenize(string raw)
    {
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        foreach (char c in raw)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                current.Append(c);
                continue;
            }

            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    private static bool TryParseKind(string value, out FileKind kind)
    {
        // Accepts the enum names plus the aliases people reach for first.
        switch (value.ToLowerInvariant())
        {
            case "image":
            case "picture":
            case "photo":
                kind = FileKind.Image;
                return true;

            case "video":
            case "movie":
                kind = FileKind.Video;
                return true;

            case "audio":
            case "music":
                kind = FileKind.Audio;
                return true;

            case "doc":
            case "document":
                kind = FileKind.Document;
                return true;

            case "archive":
            case "zip":
                kind = FileKind.Archive;
                return true;

            case "pdf":
                kind = FileKind.Pdf;
                return true;

            case "epub":
            case "book":
                kind = FileKind.Epub;
                return true;

            case "text":
                kind = FileKind.Text;
                return true;

            case "code":
                kind = FileKind.Code;
                return true;

            default:
                kind = FileKind.Other;
                return false;
        }
    }

    private static bool TryParseSizeFilter(string value, string name, out long bytes)
    {
        bytes = 0;

        if (!value.StartsWith($"{name}=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return TryParseSize(value[(name.Length + 1)..], out bytes);
    }

    /// <summary>
    /// Reads sizes written as <c>50M</c>, <c>1.5G</c> or a plain byte count. Suffixes are binary
    /// (M = 1024², matching how the sizes are displayed).
    /// </summary>
    internal static bool TryParseSize(string text, out long bytes)
    {
        bytes = 0;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string trimmed = text.Trim();

        // A trailing "B" is dropped first so "50MB" and "50M" parse identically. Checking the
        // unit letter before doing this would see the B and treat the whole string as bytes.
        if (trimmed.EndsWith('B') || trimmed.EndsWith('b'))
        {
            trimmed = trimmed[..^1];
        }

        if (trimmed.Length == 0)
        {
            return false;
        }

        long multiplier = char.ToUpperInvariant(trimmed[^1]) switch
        {
            'K' => 1024L,
            'M' => 1024L * 1024,
            'G' => 1024L * 1024 * 1024,
            'T' => 1024L * 1024 * 1024 * 1024,
            _ => 1,
        };

        string number = multiplier == 1 ? trimmed : trimmed[..^1];

        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            || parsed < 0)
        {
            return false;
        }

        bytes = (long)(parsed * multiplier);

        return true;
    }

    private static bool TryParseDateFilter(string value, string name, out DateTime? utc)
    {
        utc = null;

        if (!value.StartsWith($"{name}=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return TryParseDate(value[(name.Length + 1)..], out utc);
    }

    /// <summary>
    /// Reads either an absolute date (<c>2024-01-01</c>) or a relative age (<c>7d</c>,
    /// <c>12h</c>, <c>3w</c>). Relative ages are far more useful for "what changed lately?".
    /// </summary>
    internal static bool TryParseDate(string text, out DateTime? utc)
    {
        utc = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string trimmed = text.Trim();

        if (DateTime.TryParse(
            trimmed,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var absolute))
        {
            utc = absolute;
            return true;
        }

        char unit = char.ToLowerInvariant(trimmed[^1]);
        string number = trimmed[..^1];

        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double amount)
            || amount < 0)
        {
            return false;
        }

        TimeSpan? span = unit switch
        {
            'h' => TimeSpan.FromHours(amount),
            'd' => TimeSpan.FromDays(amount),
            'w' => TimeSpan.FromDays(amount * 7),
            'm' => TimeSpan.FromDays(amount * 30),
            'y' => TimeSpan.FromDays(amount * 365),
            _ => null,
        };

        if (span is null)
        {
            return false;
        }

        utc = DateTime.UtcNow - span.Value;

        return true;
    }
}
