using ArcaFS.Services.FileSystem;
using ArcaFS.Services.Uploads;

namespace ArcaFS.Tests.Uploads;

public class ImageResizerTests
{
    [Theory]
    [InlineData(4000, 3000, 2560, 2560, true)]
    [InlineData(2560, 2560, 2560, 2560, false)]
    [InlineData(100, 100, 2560, 2560, false)]
    [InlineData(100, 4000, 0, 2560, true)]
    [InlineData(100, 100, 0, 0, false)]
    public void NeedsShrink_OnlyWhenAnAxisExceedsItsLimit(int width, int height, int maxWidth, int maxHeight, bool expected) =>
        Assert.Equal(expected, ImageResizer.NeedsShrink(width, height, maxWidth, maxHeight));

    [Theory]
    [InlineData("photo.jpg", true)]
    [InlineData("drawing.svg", false)]
    [InlineData("clip.mp4", false)]
    [InlineData("notes.txt", false)]
    public void CanShrink_SkipsSvgAndNonImages(string fileName, bool expected) =>
        Assert.Equal(expected, ImageResizer.CanShrink(fileName));
}

public class FreeNameTests
{
    [Fact]
    public void Reserve_SkipsNamesAlreadyTaken()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "notes.txt", "notes (2).txt" };

        string reserved = FreeNames.Reserve("notes.txt", isDirectory: false, taken);

        Assert.Equal("notes (3).txt", reserved);
        Assert.Contains("notes (3).txt", taken);
    }
}
