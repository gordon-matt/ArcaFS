using ArcaFS.Services.FileSystem;

namespace ArcaFS.Tests.FileSystem;

public class ExcludeMatcherTests
{
    private static int nextSourceId = 1000;

    /// <summary>
    /// Each test gets a distinct source id because <see cref="ExcludeMatcher"/> caches per source.
    /// </summary>
    private static Source SourceWith(string? patterns) => new()
    {
        Id = Interlocked.Increment(ref nextSourceId),
        Name = "Test",
        Path = "/data",
        ExcludePatterns = patterns,
    };

    [Fact]
    public void ExcludesNothingWhenNoPatternsAreConfigured()
    {
        var matcher = ExcludeMatcher.For(SourceWith(null));

        Assert.False(matcher.IsExcluded("anything.txt"));
        Assert.False(matcher.IsExcluded(".hidden"));
    }

    [Theory]
    [InlineData("node_modules", "node_modules", true)]
    [InlineData("node_modules", "node_modules2", false)]
    [InlineData("*.tmp", "scratch.tmp", true)]
    [InlineData("*.tmp", "scratch.txt", false)]
    [InlineData("~$*", "~$budget.xlsx", true)]
    [InlineData("file?.log", "file1.log", true)]
    [InlineData("file?.log", "file12.log", false)]
    public void MatchesGlobsAnchoredToTheWholeName(string pattern, string name, bool expected)
    {
        var matcher = ExcludeMatcher.For(SourceWith(pattern));

        Assert.Equal(expected, matcher.IsExcluded(name));
    }

    [Fact]
    public void AppliesAnyOfSeveralPatterns()
    {
        var matcher = ExcludeMatcher.For(SourceWith("*.tmp\n*.bak\nThumbs.db"));

        Assert.True(matcher.IsExcluded("draft.tmp"));
        Assert.True(matcher.IsExcluded("draft.bak"));
        Assert.True(matcher.IsExcluded("Thumbs.db"));
        Assert.False(matcher.IsExcluded("draft.docx"));
    }

    [Fact]
    public void IgnoresCommentsAndBlankLines()
    {
        var matcher = ExcludeMatcher.For(SourceWith("# editor leftovers\n\n*.swp\n\n"));

        Assert.True(matcher.IsExcluded("notes.swp"));
        Assert.False(matcher.IsExcluded("# editor leftovers"));
    }

    [Fact]
    public void MatchesCaseInsensitively()
    {
        var matcher = ExcludeMatcher.For(SourceWith("thumbs.db"));

        Assert.True(matcher.IsExcluded("Thumbs.db"));
        Assert.True(matcher.IsExcluded("THUMBS.DB"));
    }

    [Fact]
    public void TreatsRegexMetacharactersAsLiteralText()
    {
        // A user typing "a+b.txt" means a file called exactly that, not a regex quantifier.
        var matcher = ExcludeMatcher.For(SourceWith("a+b.txt"));

        Assert.True(matcher.IsExcluded("a+b.txt"));
        Assert.False(matcher.IsExcluded("aab.txt"));
    }

    [Fact]
    public void DoesNotLetADotInAPatternMatchAnyCharacter()
    {
        var matcher = ExcludeMatcher.For(SourceWith("config.json"));

        Assert.True(matcher.IsExcluded("config.json"));
        Assert.False(matcher.IsExcluded("configXjson"));
    }

    [Fact]
    public void RebuildsWhenASourcesPatternsChange()
    {
        var source = SourceWith("*.tmp");
        Assert.True(ExcludeMatcher.For(source).IsExcluded("a.tmp"));

        source.ExcludePatterns = "*.bak";

        Assert.False(ExcludeMatcher.For(source).IsExcluded("a.tmp"));
        Assert.True(ExcludeMatcher.For(source).IsExcluded("a.bak"));
    }
}
