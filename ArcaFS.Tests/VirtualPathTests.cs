namespace ArcaFS.Tests;

public class VirtualPathTests
{
    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("   ", "/")]
    [InlineData("/", "/")]
    [InlineData("a", "/a")]
    [InlineData("/a/", "/a")]
    [InlineData("//a//b//", "/a/b")]
    [InlineData("\\a\\b", "/a/b")]
    [InlineData("/a/./b", "/a/b")]
    [InlineData("/a/b/..", "/a")]
    [InlineData("/a/../b", "/b")]
    public void Normalize_Canonicalises(string? input, string expected) =>
        Assert.Equal(expected, VirtualPath.Normalize(input));

    [Theory]
    [InlineData("..")]
    [InlineData("../..")]
    [InlineData("/../etc/passwd")]
    [InlineData("/a/../../..")]
    [InlineData("....//....//")]
    public void Normalize_LeavesNoTraversalSegments(string input)
    {
        string result = VirtualPath.Normalize(input);

        // A segment of exactly ".." is a traversal; a segment merely containing dots (such as
        // a directory literally named "....") is an ordinary name and must survive intact.
        Assert.DoesNotContain("..", result.Split('/'), StringComparer.Ordinal);
        Assert.StartsWith("/", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Normalize_TraversalResolvesToRootNotOutside() =>
        Assert.Equal("/etc/passwd", VirtualPath.Normalize("/../../../etc/passwd"));

    [Theory]
    [InlineData("/", 0)]
    [InlineData("/a", 1)]
    [InlineData("/a/b/c", 3)]
    public void Depth_CountsSegments(string path, int expected) =>
        Assert.Equal(expected, VirtualPath.Depth(path));

    [Theory]
    [InlineData("/a/b", "/a")]
    [InlineData("/a", "/")]
    [InlineData("/", null)]
    public void GetParent_WalksUp(string path, string? expected) =>
        Assert.Equal(expected, VirtualPath.GetParent(path));

    [Theory]
    [InlineData("/a/b", "b")]
    [InlineData("/a", "a")]
    [InlineData("/", "")]
    public void GetName_ReturnsLastSegment(string path, string expected) =>
        Assert.Equal(expected, VirtualPath.GetName(path));

    [Theory]
    [InlineData("/a/b", "/a", true)]
    [InlineData("/a", "/a", true)]
    [InlineData("/ab", "/a", false)]
    [InlineData("/a", "/a/b", false)]
    [InlineData("/anything", "/", true)]
    public void IsSameOrUnder_ComparesWholeSegments(string candidate, string ancestor, bool expected) =>
        Assert.Equal(expected, VirtualPath.IsSameOrUnder(candidate, ancestor));

    [Theory]
    [InlineData("/scope/a/b", "/scope", "/a/b")]
    [InlineData("/scope", "/scope", "/")]
    [InlineData("/other", "/scope", null)]
    [InlineData("/a/b", "/", "/a/b")]
    public void MakeRelativeTo_StripsTheScopePrefix(string candidate, string scope, string? expected) =>
        Assert.Equal(expected, VirtualPath.MakeRelativeTo(candidate, scope));

    [Fact]
    public void ExpandTokens_SubstitutesUserNameAndId()
    {
        string expanded = VirtualPath.ExpandTokens(
            $"/{Constants.PathTokens.UserName}/{Constants.PathTokens.UserId}",
            "alice",
            "u-1");

        Assert.Equal("/alice/u-1", expanded);
    }

    [Fact]
    public void ExpandTokens_SanitisesSeparatorsSoATokenCannotWidenAPath()
    {
        // A display name containing a slash must not turn "/${username}" into two segments.
        string expanded = VirtualPath.ExpandTokens($"/{Constants.PathTokens.UserName}", "../../etc", "u-1");

        Assert.Equal("/.._.._etc", expanded);
        Assert.Equal("/.._.._etc", VirtualPath.Normalize(expanded));
    }

    [Fact]
    public void HasTokens_DetectsUnexpandedPlaceholders()
    {
        Assert.True(VirtualPath.HasTokens($"/{Constants.PathTokens.UserName}"));
        Assert.False(VirtualPath.HasTokens("/alice"));
    }

    [Fact]
    public void TryResolve_MapsInsideTheSourceRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "arcafs-test-root");

        Assert.True(VirtualPath.TryResolve(root, "/a/b.txt", out string? physical));
        Assert.StartsWith(Path.GetFullPath(root), physical, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("b.txt", physical, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/../../../etc/passwd")]
    [InlineData("/..%2f..%2fetc")]
    [InlineData("/a/../../../../..")]
    public void TryResolve_StaysInsideTheSourceRoot(string attempt)
    {
        string root = Path.Combine(Path.GetTempPath(), "arcafs-test-root");

        Assert.True(VirtualPath.TryResolve(root, attempt, out string? physical));
        Assert.StartsWith(Path.GetFullPath(root), physical, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromPhysical_RoundTrips()
    {
        string root = Path.Combine(Path.GetTempPath(), "arcafs-test-root");
        Assert.True(VirtualPath.TryResolve(root, "/a/b/c.txt", out string? physical));

        Assert.Equal("/a/b/c.txt", VirtualPath.FromPhysical(root, physical));
    }
}