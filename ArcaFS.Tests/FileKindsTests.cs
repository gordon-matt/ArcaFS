namespace ArcaFS.Tests;

public class FileKindsTests
{
    [Theory]
    [InlineData("photo.JPG", FileKind.Image)]
    [InlineData("clip.mkv", FileKind.Video)]
    [InlineData("song.flac", FileKind.Audio)]
    [InlineData("manual.pdf", FileKind.Pdf)]
    [InlineData("novel.epub", FileKind.Epub)]
    [InlineData("sheet.xlsx", FileKind.Document)]
    [InlineData("backup.tar.gz", FileKind.Archive)]
    [InlineData("notes.md", FileKind.Text)]
    [InlineData("Program.cs", FileKind.Code)]
    [InlineData("component.ts", FileKind.Code)]
    [InlineData("mystery.qqq", FileKind.Other)]
    [InlineData("LICENSE", FileKind.Other)]
    public void ClassifiesByExtensionCaseInsensitively(string fileName, FileKind expected) =>
        Assert.Equal(expected, FileKinds.Classify(fileName, isDirectory: false));

    [Fact]
    public void ClassifiesDirectoriesAsFolderRegardlessOfName() =>
        Assert.Equal(FileKind.Folder, FileKinds.Classify("archive.zip", isDirectory: true));

    [Theory]
    [InlineData("image/png", true)]
    [InlineData("image/jpeg", true)]
    [InlineData("video/mp4", true)]
    [InlineData("audio/mpeg", true)]
    [InlineData("application/pdf", true)]
    [InlineData("text/plain", true)]
    public void AllowsMediaToBeServedInline(string mimeType, bool expected) =>
        Assert.Equal(expected, FileKinds.IsInlineSafe(mimeType));

    [Theory]
    [InlineData("text/html")]
    [InlineData("image/svg+xml")]
    [InlineData("application/xml")]
    [InlineData("text/javascript")]
    [InlineData("application/octet-stream")]
    [InlineData("")]
    [InlineData(null)]
    public void RefusesToServeScriptableOrUnknownContentInline(string? mimeType) =>
        // Serving these inline from the app's own origin would turn any upload into stored XSS,
        // so they must always go out as attachments.
        Assert.False(FileKinds.IsInlineSafe(mimeType));

    [Theory]
    [InlineData("Program.cs", "csharp")]
    [InlineData("app.tsx", "typescript")]
    [InlineData("docker-compose.yml", "yaml")]
    [InlineData("readme.md", "markdown")]
    [InlineData("unknown.qqq", "plaintext")]
    [InlineData("LICENSE", "plaintext")]
    public void MapsExtensionsToEditorLanguages(string fileName, string expected) =>
        Assert.Equal(expected, FileKinds.GetEditorLanguage(fileName));

    [Fact]
    public void CanPreview_CoversMediaAndText()
    {
        Assert.True(FileKinds.CanPreview(FileKind.Image));
        Assert.True(FileKinds.CanPreview(FileKind.Pdf));
        Assert.True(FileKinds.CanPreview(FileKind.Text));
        Assert.True(FileKinds.CanPreview(FileKind.Code));
        Assert.False(FileKinds.CanPreview(FileKind.Archive));
        Assert.False(FileKinds.CanPreview(FileKind.Folder));
    }

    [Fact]
    public void OnlyOpensTextAndCodeInTheEditor()
    {
        Assert.True(FileKinds.IsEditable(FileKind.Text));
        Assert.True(FileKinds.IsEditable(FileKind.Code));

        Assert.False(FileKinds.IsEditable(FileKind.Image));
        Assert.False(FileKinds.IsEditable(FileKind.Pdf));
        Assert.False(FileKinds.IsEditable(FileKind.Other));
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(-1, "-")]
    public void FormatsSizesUsingBinaryUnits(long bytes, string expected) =>
        Assert.Equal(expected, FileKinds.FormatSize(bytes));

    [Fact]
    public void FallsBackToOctetStreamForUnknownExtensions() =>
        Assert.Equal(FileKinds.DefaultMimeType, FileKinds.GetMimeType("mystery.qqq"));
}