using ArcaFS.Services.Shares;

namespace ArcaFS.Tests.Shares;

public class ShareSessionTests
{
    private static ShareSession FolderShare(string rootPath, ShareMode mode = ShareMode.ReadOnly) => new()
    {
        Token = "t",
        SourceId = 1,
        RootPath = rootPath,
        IsSingleFile = false,
        Mode = mode,
        Principal = AccessPrincipal.Anonymous with { Ceiling = ShareSession.CeilingFor(mode) },
    };

    private static ShareSession FileShare(string rootPath) => new()
    {
        Token = "t",
        SourceId = 1,
        RootPath = rootPath,
        IsSingleFile = true,
        Mode = ShareMode.ReadOnly,
        Principal = AccessPrincipal.Anonymous,
    };

    [Theory]
    [InlineData(null, "/projects/public")]
    [InlineData("", "/projects/public")]
    [InlineData("/", "/projects/public")]
    [InlineData("docs", "/projects/public/docs")]
    [InlineData("/docs", "/projects/public/docs")]
    [InlineData("/docs/notes.txt", "/projects/public/docs/notes.txt")]
    [InlineData("docs//notes.txt", "/projects/public/docs/notes.txt")]
    public void RebasesRelativePathsOntoTheSharedRoot(string? relative, string expected)
    {
        var session = FolderShare("/projects/public");

        Assert.Equal(expected, session.ResolveWithinShare(relative));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../")]
    [InlineData("../secrets")]
    [InlineData("/../secrets")]
    [InlineData("docs/../../secrets")]
    [InlineData("../../../../etc/passwd")]
    [InlineData("....//....//secrets")]
    [InlineData("%2e%2e/secrets")]
    public void CannotBeTalkedOutOfTheSharedSubtree(string relative)
    {
        var session = FolderShare("/projects/public");

        string? resolved = session.ResolveWithinShare(relative);

        // Either refused outright, or normalised to something still inside the share. Both are
        // safe; escaping is not.
        if (resolved is not null)
        {
            Assert.True(
                VirtualPath.IsSameOrUnder(resolved, "/projects/public"),
                $"'{relative}' resolved to '{resolved}', which is outside the share.");
        }
    }

    [Fact]
    public void DoesNotTreatASiblingWithASharedPrefixAsInsideTheShare()
    {
        // "/projects/public-archive" starts with "/projects/public" as a string but is a
        // different folder, so a share of the latter must not reach it.
        var session = FolderShare("/projects/public");

        string? resolved = session.ResolveWithinShare("/../public-archive/secret.txt");

        if (resolved is not null)
        {
            Assert.StartsWith("/projects/public/", resolved, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AllowsOnlyTheSharedFileItselfForASingleFileShare()
    {
        var session = FileShare("/projects/public/report.pdf");

        Assert.Equal("/projects/public/report.pdf", session.ResolveWithinShare(null));
        Assert.Equal("/projects/public/report.pdf", session.ResolveWithinShare("/"));

        // A single-file link has no subtree, so any sub-path request is bogus.
        Assert.Null(session.ResolveWithinShare("sibling.pdf"));
        Assert.Null(session.ResolveWithinShare("../other.pdf"));
    }

    [Fact]
    public void WorksWhenTheWholeSourceIsShared()
    {
        var session = FolderShare(Constants.RootPath);

        Assert.Equal("/", session.ResolveWithinShare("/"));
        Assert.Equal("/a/b", session.ResolveWithinShare("a/b"));
    }

    [Fact]
    public void ReadOnlyModeGrantsNoWritePermissions()
    {
        var ceiling = ShareSession.CeilingFor(ShareMode.ReadOnly);

        Assert.True(ceiling.Has(FilePermission.Browse));
        Assert.True(ceiling.Has(FilePermission.Read));
        Assert.True(ceiling.Has(FilePermission.Download));

        Assert.False(ceiling.HasAny(
            FilePermission.Create | FilePermission.Modify | FilePermission.Delete
            | FilePermission.Rename | FilePermission.Move));
    }

    [Fact]
    public void UploadModeAddsCreateButNotModifyOrDelete()
    {
        var ceiling = ShareSession.CeilingFor(ShareMode.Upload);

        Assert.True(ceiling.Has(FilePermission.Create));

        // A drop-box link must not let a recipient overwrite or remove what is already there.
        Assert.False(ceiling.HasAny(FilePermission.Modify | FilePermission.Delete));
    }

    [Fact]
    public void ReadWriteModeAddsTheMutatingPermissions()
    {
        var ceiling = ShareSession.CeilingFor(ShareMode.ReadWrite);

        Assert.True(ceiling.Has(
            FilePermission.Create | FilePermission.Modify | FilePermission.Delete
            | FilePermission.Rename | FilePermission.Move));
    }

    [Fact]
    public void NoModeEverGrantsSharingOrPermissionManagement()
    {
        // A share must not be re-shareable, and certainly must not let a visitor rewrite the ACL.
        foreach (var mode in Enum.GetValues<ShareMode>())
        {
            var ceiling = ShareSession.CeilingFor(mode);

            Assert.False(
                ceiling.HasAny(FilePermission.Share | FilePermission.ManagePermissions),
                $"{mode} must not grant Share or ManagePermissions.");
        }
    }

    [Fact]
    public void VisitorPrincipalIsAnonymousAndNotAnAdministrator()
    {
        var session = FolderShare("/x", ShareMode.ReadWrite);

        Assert.False(session.Principal.IsAuthenticated);
        Assert.False(session.Principal.IsAdministrator);
        Assert.Equal(Constants.AnonymousUserId, session.Principal.UserId);
        Assert.Empty(session.Principal.RoleNames);
        Assert.Empty(session.Principal.GroupIds);
    }
}