using ArcaFS.Services.Search;

namespace ArcaFS.Tests.Search;

public class SearchQueryParserTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TreatsBlankInputAsAnEmptyQuery(string? raw)
    {
        var query = SearchQueryParser.Parse(raw);

        Assert.True(query.IsEmpty);
        Assert.Empty(query.Terms);
    }

    [Fact]
    public void SplitsPlainWordsIntoTerms()
    {
        var query = SearchQueryParser.Parse("annual report");

        Assert.Equal(["annual", "report"], query.Terms);
        Assert.True(query.RequireAllTerms);
        Assert.False(query.ExactPhrase);
    }

    [Fact]
    public void KeepsAQuotedPhraseTogether()
    {
        var query = SearchQueryParser.Parse("\"annual report\"");

        Assert.Equal(["annual report"], query.Terms);
        Assert.True(query.ExactPhrase);
    }

    [Fact]
    public void TreatsPipeAlternationAsAnyRatherThanAll()
    {
        var query = SearchQueryParser.Parse("draft|final");

        Assert.Equal(["draft", "final"], query.Terms);
        Assert.False(query.RequireAllTerms);
    }

    [Theory]
    [InlineData("type:image", FileKind.Image)]
    [InlineData("type:photo", FileKind.Image)]
    [InlineData("type:video", FileKind.Video)]
    [InlineData("type:movie", FileKind.Video)]
    [InlineData("type:music", FileKind.Audio)]
    [InlineData("type:pdf", FileKind.Pdf)]
    [InlineData("type:book", FileKind.Epub)]
    [InlineData("type:code", FileKind.Code)]
    public void RecognisesKindFiltersAndTheirAliases(string raw, FileKind expected)
    {
        var query = SearchQueryParser.Parse(raw);

        Assert.Equal([expected], query.Kinds);
        Assert.Empty(query.Terms);
    }

    [Fact]
    public void DistinguishesFolderFromFileFilters()
    {
        Assert.True(SearchQueryParser.Parse("type:folder").FoldersOnly);
        Assert.False(SearchQueryParser.Parse("type:file").FoldersOnly);
        Assert.Null(SearchQueryParser.Parse("type:image").FoldersOnly);
    }

    [Theory]
    [InlineData("50", 50L)]
    [InlineData("1K", 1024L)]
    [InlineData("1KB", 1024L)]
    [InlineData("50M", 52_428_800L)]
    [InlineData("1.5G", 1_610_612_736L)]
    [InlineData("2T", 2_199_023_255_552L)]
    public void ReadsSizesWithBinarySuffixes(string text, long expected)
    {
        Assert.True(SearchQueryParser.TryParseSize(text, out long bytes));
        Assert.Equal(expected, bytes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-5M")]
    public void RejectsUnparseableSizes(string text) =>
        Assert.False(SearchQueryParser.TryParseSize(text, out _));

    [Fact]
    public void AppliesSizeBoundsFromTypeFilters()
    {
        var query = SearchQueryParser.Parse("type:largerThan=50M type:smallerThan=1G");

        Assert.Equal(52_428_800L, query.LargerThanBytes);
        Assert.Equal(1_073_741_824L, query.SmallerThanBytes);
        Assert.Empty(query.Terms);
    }

    [Fact]
    public void ReadsAnAbsoluteDateAsUtc()
    {
        Assert.True(SearchQueryParser.TryParseDate("2024-01-15", out var utc));

        Assert.NotNull(utc);
        Assert.Equal(new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc), utc!.Value);
    }

    [Theory]
    [InlineData("12h", 0.5)]
    [InlineData("7d", 7)]
    [InlineData("2w", 14)]
    public void ReadsRelativeAgesAsAnOffsetFromNow(string text, double expectedDaysAgo)
    {
        var before = DateTime.UtcNow;

        Assert.True(SearchQueryParser.TryParseDate(text, out var utc));
        Assert.NotNull(utc);

        double actualDaysAgo = (before - utc!.Value).TotalDays;

        Assert.Equal(expectedDaysAgo, actualDaysAgo, precision: 2);
    }

    [Fact]
    public void AppliesDateBoundsFromTypeFilters()
    {
        var query = SearchQueryParser.Parse("type:newerThan=2024-01-01 type:olderThan=2025-01-01");

        Assert.Equal(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), query.NewerThanUtc);
        Assert.Equal(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), query.OlderThanUtc);
    }

    [Fact]
    public void EnablesCaseSensitivityOnRequest()
    {
        Assert.True(SearchQueryParser.Parse("README case:exact").CaseSensitive);
        Assert.False(SearchQueryParser.Parse("README").CaseSensitive);
    }

    [Fact]
    public void CombinesTermsAndFiltersInOneQuery()
    {
        var query = SearchQueryParser.Parse("holiday type:image type:largerThan=2M");

        Assert.Equal(["holiday"], query.Terms);
        Assert.Equal([FileKind.Image], query.Kinds);
        Assert.Equal(2_097_152L, query.LargerThanBytes);
        Assert.False(query.IsEmpty);
    }

    [Fact]
    public void KeepsAnUnrecognisedFilterOutOfTheTermsRatherThanSearchingForIt()
    {
        // "type:nonsense" is a filter the user got wrong, not a filename. Searching for the
        // literal text would return nothing and look like the index was broken.
        var query = SearchQueryParser.Parse("type:nonsense");

        Assert.Empty(query.Terms);
        Assert.Empty(query.Kinds);
        Assert.True(query.IsEmpty);
    }

    [Fact]
    public void TreatsAColonInAFilenameAsAnOrdinaryTerm()
    {
        var query = SearchQueryParser.Parse("notes:draft");

        Assert.Equal(["notes:draft"], query.Terms);
    }

    [Fact]
    public void DoesNotRequireAllTermsForAQuotedPhrase()
    {
        // The phrase is one substring to find, so the all-terms flag would be meaningless.
        var query = SearchQueryParser.Parse("\"quarterly results\"");

        Assert.False(query.RequireAllTerms);
        Assert.True(query.ExactPhrase);
    }
}
