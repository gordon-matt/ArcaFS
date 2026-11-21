using ArcaFS.Services.FileSystem;

namespace ArcaFS.Tests.FileSystem;

public class TextConflictTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), "arcafs-text-" + Guid.NewGuid().ToString("N") + ".txt");

    [Fact]
    public void WasChangedSince_IgnoresAMissingExpectation()
    {
        File.WriteAllText(path, "hello");

        Assert.False(FileSystemService.WasChangedSince(path, null));
    }

    [Fact]
    public void WasChangedSince_AllowsTheStampTheEditorOpened()
    {
        File.WriteAllText(path, "hello");
        DateTime opened = File.GetLastWriteTimeUtc(path);

        Assert.False(FileSystemService.WasChangedSince(path, opened));
    }

    [Fact]
    public void WasChangedSince_RefusesAStampFromBeforeTheFileChanged()
    {
        File.WriteAllText(path, "hello");

        Assert.True(FileSystemService.WasChangedSince(path, DateTime.UtcNow.AddMinutes(-5)));
    }

    public void Dispose()
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
