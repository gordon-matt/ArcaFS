namespace ArcaFS.Tests;

public class UserHomePathTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "arcafs-home-" + Guid.NewGuid().ToString("N"));

    public UserHomePathTests()
    {
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Align_PrefersExistingFolderMatchingExpandedScopeCasing()
    {
        Directory.CreateDirectory(Path.Combine(root, "Matt"));

        string scope = UserHomePath.AlignScopeWithExistingDirectory(
            root,
            "/matt",
            "matt",
            "admin");

        Assert.Equal("/Matt", scope);
    }

    [Fact]
    public void Align_FallsBackToDisplayNameWhenLoginFolderIsMissing()
    {
        Directory.CreateDirectory(Path.Combine(root, "Matt"));

        string scope = UserHomePath.AlignScopeWithExistingDirectory(
            root,
            "/admin",
            "admin",
            "Matt");

        Assert.Equal("/Matt", scope);
    }

    [Fact]
    public void Align_KeepsExpandedScopeWhenNothingOnDiskMatches()
    {
        string scope = UserHomePath.AlignScopeWithExistingDirectory(
            root,
            "/admin",
            "admin",
            "Matt");

        Assert.Equal("/admin", scope);
    }

    [Fact]
    public void Align_LeavesRootAlone()
    {
        Directory.CreateDirectory(Path.Combine(root, "Matt"));

        Assert.Equal(
            "/",
            UserHomePath.AlignScopeWithExistingDirectory(root, "/", "admin", "Matt"));
    }
}