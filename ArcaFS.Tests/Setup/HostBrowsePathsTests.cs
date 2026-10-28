using ArcaFS.Services.Setup;

namespace ArcaFS.Tests.Setup;

public class HostBrowsePathsTests
{
    [Fact]
    public void TryGetEnforcedRoot_MissingDirectory_IsNotEnforced()
    {
        string missing = Path.Combine(Path.GetTempPath(), "arcafs-missing-" + Guid.NewGuid().ToString("N"));

        bool enforced = HostBrowsePaths.TryGetEnforcedRoot(missing, out _);

        Assert.False(enforced);
    }

    [Fact]
    public void TryGetEnforcedRoot_ExistingDirectory_IsEnforced()
    {
        string root = CreateTempRoot();

        try
        {
            bool enforced = HostBrowsePaths.TryGetEnforcedRoot(root, out string resolved);

            Assert.True(enforced);
            Assert.Equal(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar), resolved);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IsInside_ChildAndRoot_AreInside_SiblingIsNot()
    {
        string root = CreateTempRoot();
        string child = Path.Combine(root, "files");
        Directory.CreateDirectory(child);
        string sibling = Path.Combine(Path.GetDirectoryName(root)!, "arcafs-other-" + Guid.NewGuid().ToString("N"));

        try
        {
            Assert.True(HostBrowsePaths.IsInside(root, root));
            Assert.True(HostBrowsePaths.IsInside(child, root));
            Assert.True(HostBrowsePaths.IsInside(Path.Combine(child, "..", "files"), root));
            Assert.False(HostBrowsePaths.IsInside(Path.Combine(child, "..", ".."), root));
            Assert.False(HostBrowsePaths.IsInside(sibling, root));
            Assert.False(HostBrowsePaths.IsInside(root + "-extra", root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ParentWithinRoot_AtRoot_IsNull_AndChildParent_IsRoot()
    {
        string root = CreateTempRoot();
        string child = Path.Combine(root, "files");
        Directory.CreateDirectory(child);

        try
        {
            Assert.Null(HostBrowsePaths.ParentWithinRoot(root, root));
            Assert.Equal(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)),
                HostBrowsePaths.ParentWithinRoot(child, root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IsAllowed_SymlinkOutsideRoot_IsRejected()
    {
        string root = CreateTempRoot();
        string outside = CreateTempRoot();
        string link = Path.Combine(root, "escape");

        try
        {
            Directory.CreateSymbolicLink(link, outside);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(outside, recursive: true);
            return;
        }

        try
        {
            Assert.True(HostBrowsePaths.IsInside(link, root));
            Assert.False(HostBrowsePaths.IsAllowed(link, root));
            Assert.True(HostBrowsePaths.IsAllowed(root, root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }

    private static string CreateTempRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "arcafs-browse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
