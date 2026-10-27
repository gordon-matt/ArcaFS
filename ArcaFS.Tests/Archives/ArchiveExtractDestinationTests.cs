using ArcaFS.Services.Archives;

namespace ArcaFS.Tests.Archives;

/// <summary>
/// Extracting a compressed file should put it back in the current folder, not inside a new
/// folder named after the archive, and should refuse rather than throw when the file is already
/// there.
/// </summary>
public class ArchiveExtractDestinationTests : IDisposable
{
    private readonly string root = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "arcafs-extract-dest-" + Guid.NewGuid().ToString("N")));

    public ArchiveExtractDestinationTests()
    {
        Directory.CreateDirectory(root);
    }

    [Theory]
    [InlineData("/docs/notes.zip", "/docs")]
    [InlineData("/notes.zip", "/")]
    [InlineData("/a/b/c/photos.tar.gz", "/a/b/c")]
    public void DefaultsToTheFolderThatContainsTheArchive(string archivePath, string expected)
    {
        string destination = ArchiveService.DefaultExtractDestination(archivePath, requestedDestination: null);

        Assert.Equal(expected, destination);
    }

    [Theory]
    [InlineData("/docs/out", "/docs/out")]
    [InlineData("docs/out", "/docs/out")]
    [InlineData(null, "/docs")]
    [InlineData(" ", "/docs")]
    public void HonoursAnExplicitDestinationOtherwise(string? requested, string expected)
    {
        string destination = ArchiveService.DefaultExtractDestination("/docs/notes.zip", requested);

        Assert.Equal(expected, destination);
    }

    [Fact]
    public void ReportsAFileThatAlreadyExistsAtTheExtractTarget()
    {
        File.WriteAllText(Path.Combine(root, "notes.txt"), "already here");

        var conflicts = ArchiveService.FindConflictingExtractNames(
            root,
            [("notes.txt", false)]);

        Assert.Equal(["notes.txt"], conflicts);
        Assert.Equal(
            "'notes.txt' already exists here.",
            ArchiveService.FormatAlreadyExistsMessage(conflicts));
    }

    [Fact]
    public void DoesNotTreatAMissingFileAsAConflict()
    {
        var conflicts = ArchiveService.FindConflictingExtractNames(
            root,
            [("notes.txt", false)]);

        Assert.Empty(conflicts);
    }

    [Fact]
    public void TreatsAnExistingFolderWithTheSameNameAsAConflictForAFile()
    {
        Directory.CreateDirectory(Path.Combine(root, "notes.txt"));

        var conflicts = ArchiveService.FindConflictingExtractNames(
            root,
            [("notes.txt", false)]);

        Assert.Equal(["notes.txt"], conflicts);
    }

    [Fact]
    public void AllowsExtractingIntoAnExistingFolderFromTheArchive()
    {
        Directory.CreateDirectory(Path.Combine(root, "docs"));

        var conflicts = ArchiveService.FindConflictingExtractNames(
            root,
            [("docs/", true), ("docs/notes.txt", false)]);

        Assert.Empty(conflicts);
    }

    [Fact]
    public void ReportsAFileBlockingADirectoryEntry()
    {
        File.WriteAllText(Path.Combine(root, "docs"), "not a folder");

        var conflicts = ArchiveService.FindConflictingExtractNames(
            root,
            [("docs/", true)]);

        Assert.Equal(["docs"], conflicts);
    }

    [Fact]
    public void NamesOnlyASingleConflict()
    {
        Directory.CreateDirectory(Path.Combine(root, "photos"));
        File.WriteAllText(Path.Combine(root, "photos", "a.txt"), "a");

        var conflicts = ArchiveService.FindConflictingExtractNames(
            root,
            [("photos/a.txt", false), ("photos/b.txt", false)]);

        Assert.Equal(["a.txt"], conflicts);
        Assert.Equal(
            "'a.txt' already exists here.",
            ArchiveService.FormatAlreadyExistsMessage(conflicts));
    }

    [Fact]
    public void SpeaksGenerallyWhenSeveralFilesWouldCollide()
    {
        File.WriteAllText(Path.Combine(root, "a.txt"), "a");
        File.WriteAllText(Path.Combine(root, "b.txt"), "b");
        File.WriteAllText(Path.Combine(root, "c.txt"), "c");

        var conflicts = ArchiveService.FindConflictingExtractNames(
            root,
            [("a.txt", false), ("b.txt", false), ("c.txt", false)]);

        Assert.Equal(3, conflicts.Count);
        Assert.Equal(
            "One or more files already exist here.",
            ArchiveService.FormatAlreadyExistsMessage(conflicts));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup of a temp folder used only by these tests.
        }
    }
}