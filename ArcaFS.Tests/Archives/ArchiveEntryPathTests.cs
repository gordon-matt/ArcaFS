using ArcaFS.Services.Archives;

namespace ArcaFS.Tests.Archives;

/// <summary>
/// Covers <c>ArchiveService.ResolveEntryDestination</c>, which decides where an archive entry is
/// allowed to land. Entry names come from the archive itself and are therefore untrusted, so
/// this is the check standing between a crafted zip and arbitrary file write (zip-slip).
/// </summary>
public class ArchiveEntryPathTests
{
    private static readonly string Root = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "arcafs-extract-root"));

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("docs/notes.txt")]
    [InlineData("docs/deep/nested/notes.txt")]
    [InlineData("./docs/notes.txt")]
    [InlineData("docs//notes.txt")]
    [InlineData("docs\\notes.txt")]
    public void AcceptsOrdinaryEntryNames(string entryKey)
    {
        string? resolved = ArchiveService.ResolveEntryDestination(Root, entryKey);

        Assert.NotNull(resolved);
        Assert.StartsWith(Root + Path.DirectorySeparatorChar, resolved!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../escaped.txt")]
    [InlineData("../../escaped.txt")]
    [InlineData("docs/../../escaped.txt")]
    [InlineData("a/b/../../../escaped.txt")]
    [InlineData("..\\escaped.txt")]
    [InlineData("docs\\..\\..\\escaped.txt")]
    public void RefusesEntriesThatClimbOutOfTheDestination(string entryKey) =>
        Assert.Null(ArchiveService.ResolveEntryDestination(Root, entryKey));

    [Theory]
    [InlineData("C:\\Windows\\System32\\evil.dll")]
    [InlineData("D:/data/evil.txt")]
    public void RefusesEntriesWithADriveLetter(string entryKey) =>
        Assert.Null(ArchiveService.ResolveEntryDestination(Root, entryKey));

    [Theory]
    [InlineData("/etc/cron.d/evil")]
    [InlineData("/root/.ssh/authorized_keys")]
    public void TreatsARootedEntryAsRelativeRatherThanAbsolute(string entryKey)
    {
        // An absolute-looking entry is stripped to a relative one and lands inside the
        // destination. It must never be written to the real filesystem root.
        string? resolved = ArchiveService.ResolveEntryDestination(Root, entryKey);

        Assert.NotNull(resolved);
        Assert.StartsWith(Root + Path.DirectorySeparatorChar, resolved!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("./")]
    [InlineData("/")]
    public void RefusesEmptyOrMeaninglessEntryNames(string entryKey) =>
        Assert.Null(ArchiveService.ResolveEntryDestination(Root, entryKey));

    [Fact]
    public void DoesNotAllowASiblingDirectorySharingThePrefix()
    {
        // "arcafs-extract-root-evil" begins with the destination path as a string. A containment
        // check done with a plain StartsWith and no separator would wrongly accept it.
        string? resolved = ArchiveService.ResolveEntryDestination(Root, "../arcafs-extract-root-evil/x.txt");

        Assert.Null(resolved);
    }

    [Fact]
    public void StripsCharactersThatWouldBeIllegalInAFileName()
    {
        string? resolved = ArchiveService.ResolveEntryDestination(Root, "docs/re:port.txt");

        Assert.NotNull(resolved);
        Assert.StartsWith(Root + Path.DirectorySeparatorChar, resolved!, StringComparison.Ordinal);

        // Whatever sanitisation does, the result must still be one path inside the destination.
        Assert.Equal(resolved, Path.GetFullPath(resolved!));
    }

    [Fact]
    public void KeepsDeeplyNestedEntriesUnderTheDestination()
    {
        string entryKey = string.Join('/', Enumerable.Repeat("level", 20)) + "/file.txt";

        string? resolved = ArchiveService.ResolveEntryDestination(Root, entryKey);

        Assert.NotNull(resolved);
        Assert.StartsWith(Root + Path.DirectorySeparatorChar, resolved!, StringComparison.Ordinal);
    }
}
