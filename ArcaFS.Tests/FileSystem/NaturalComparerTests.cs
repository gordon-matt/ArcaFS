using ArcaFS.Services.FileSystem;

namespace ArcaFS.Tests.FileSystem;

public class NaturalComparerTests
{
    [Theory]
    [InlineData("file2.txt", "file10.txt")]
    [InlineData("Episode 9", "Episode 10")]
    [InlineData("img1.png", "img2.png")]
    [InlineData("a", "b")]
    [InlineData("scan-000009.jpg", "scan-000010.jpg")]
    public void OrdersDigitRunsNumerically(string smaller, string larger)
    {
        Assert.True(NaturalComparer.Instance.Compare(smaller, larger) < 0);
        Assert.True(NaturalComparer.Instance.Compare(larger, smaller) > 0);
    }

    [Fact]
    public void SortsAWholeSequenceTheWayAPersonWould()
    {
        string[] names = ["file10.txt", "file1.txt", "file20.txt", "file2.txt", "file3.txt"];

        string[] sorted = names.OrderBy(n => n, NaturalComparer.Instance).ToArray();

        Assert.Equal(["file1.txt", "file2.txt", "file3.txt", "file10.txt", "file20.txt"], sorted);
    }

    [Fact]
    public void IgnoresLeadingZeros() =>
        // "007" and "7" name the same number, so neither sorts before the other.
        Assert.Equal(0, NaturalComparer.Instance.Compare("track007", "track7"));

    [Fact]
    public void IsCaseInsensitive() => Assert.Equal(0, NaturalComparer.Instance.Compare("ReadMe.md", "readme.md"));

    [Fact]
    public void HandlesVeryLongDigitRunsWithoutOverflowing()
    {
        // Parsing these as integers would throw or wrap; comparing run lengths does not.
        string small = new('9', 40);
        string big = "1" + new string('0', 41);

        Assert.True(NaturalComparer.Instance.Compare(small, big) < 0);
    }

    [Fact]
    public void PlacesNullsFirstWithoutThrowing()
    {
        Assert.True(NaturalComparer.Instance.Compare(null, "a") < 0);
        Assert.True(NaturalComparer.Instance.Compare("a", null) > 0);
        Assert.Equal(0, NaturalComparer.Instance.Compare(null, null));
    }

    [Fact]
    public void TreatsAPrefixAsSmallerThanTheLongerName() => Assert.True(NaturalComparer.Instance.Compare("report", "report-final") < 0);
}