using ArcaFS.Data.Entities;
using ArcaFS.Enums;
using ArcaFS.Services.Access;

namespace ArcaFS.Tests.Access;

/// <summary>
/// Behavioural spec for the permission model. The shared-folder and per-user home scenarios
/// are the ones that motivated this design, so they are the ones that must not regress.
/// </summary>
public class PermissionResolverTests
{
    private static int nextRuleId = 1;

    [Fact]
    public void DefaultPermissions_ApplyWhenNoRulesMatch()
    {
        var source = NewSource(defaultPermissions: FilePermission.ReadOnly);

        var result = PermissionResolver.Resolve(source, [], User("alice"), "/anywhere");

        Assert.Equal(FilePermission.ReadOnly, result.Effective);
    }

    [Fact]
    public void DenyByDefault_GrantsNothingWithoutRules()
    {
        var source = NewSource(denyByDefault: true);

        var result = PermissionResolver.Resolve(source, [], User("alice"), "/");

        Assert.Equal(FilePermission.None, result.Effective);
    }

    [Fact]
    public void UserRule_OverridesGroupRuleAtSamePath()
    {
        var source = NewSource(denyByDefault: true);
        var rules = new[]
        {
            Rule(AccessSubjectType.Group, "7", AccessEffect.Allow, FilePermission.ReadOnly),
            Rule(AccessSubjectType.User, "alice", AccessEffect.Allow, FilePermission.Editor),
        };

        var result = PermissionResolver.Resolve(source, rules, User("alice", groupIds: [7]), "/");

        Assert.True(result.Has(FilePermission.Delete));
    }

    [Fact]
    public void DeeperPath_OverridesShallowerPath()
    {
        var source = NewSource(denyByDefault: true);
        var rules = new[]
        {
            Rule(AccessSubjectType.Authenticated, null, AccessEffect.Allow, FilePermission.Editor),
            Rule(AccessSubjectType.Authenticated, null, AccessEffect.Deny, FilePermission.Delete, path: "/archive"),
        };

        var atRoot = PermissionResolver.Resolve(source, rules, User("alice"), "/");
        var inArchive = PermissionResolver.Resolve(source, rules, User("alice"), "/archive/2024");

        Assert.True(atRoot.Has(FilePermission.Delete));
        Assert.False(inArchive.Has(FilePermission.Delete));

        // A deny of one bit must not disturb the others inherited from the shallower rule.
        Assert.True(inArchive.Has(FilePermission.Create));
        Assert.True(inArchive.Has(FilePermission.Download));
    }

    [Fact]
    public void DenyBeatsAllow_AtEqualSpecificity()
    {
        var source = NewSource(denyByDefault: true);
        var rules = new[]
        {
            Rule(AccessSubjectType.User, "alice", AccessEffect.Allow, FilePermission.Editor),
            Rule(AccessSubjectType.User, "alice", AccessEffect.Deny, FilePermission.Delete),
        };

        var result = PermissionResolver.Resolve(source, rules, User("alice"), "/");

        Assert.False(result.Has(FilePermission.Delete));
        Assert.True(result.Has(FilePermission.Create));
    }

    [Theory]
    [InlineData(AccessPropagation.ThisPathAndChildren, "/shared", true)]
    [InlineData(AccessPropagation.ThisPathAndChildren, "/shared/sub", true)]
    [InlineData(AccessPropagation.ThisPathOnly, "/shared", true)]
    [InlineData(AccessPropagation.ThisPathOnly, "/shared/sub", false)]
    [InlineData(AccessPropagation.ChildrenOnly, "/shared", false)]
    [InlineData(AccessPropagation.ChildrenOnly, "/shared/sub", true)]
    public void Propagation_ControlsReach(AccessPropagation propagation, string target, bool expectGranted)
    {
        var source = NewSource(denyByDefault: true);
        var rules = new[]
        {
            Rule(
                AccessSubjectType.Authenticated,
                null,
                AccessEffect.Allow,
                FilePermission.ReadOnly,
                path: "/shared",
                propagation: propagation),
        };

        var result = PermissionResolver.Resolve(source, rules, User("alice"), target);

        Assert.Equal(expectGranted, result.Has(FilePermission.Browse));
    }

    [Fact]
    public void SiblingPaths_DoNotMatchOnPrefix()
    {
        var source = NewSource(denyByDefault: true);
        var rules = new[]
        {
            Rule(AccessSubjectType.Authenticated, null, AccessEffect.Allow, FilePermission.ReadOnly, path: "/photo"),
        };

        // "/photos" starts with "/photo" as a string but is a different directory.
        var result = PermissionResolver.Resolve(source, rules, User("alice"), "/photos");

        Assert.Equal(FilePermission.None, result.Effective);
    }

    [Fact]
    public void ReadOnlySource_StripsMutatingPermissions()
    {
        var source = NewSource(isReadOnly: true);
        var rules = new[]
        {
            Rule(AccessSubjectType.User, "alice", AccessEffect.Allow, FilePermission.Full),
        };

        var result = PermissionResolver.Resolve(source, rules, User("alice"), "/");

        Assert.True(result.IsClampedByReadOnlySource);
        Assert.False(result.Has(FilePermission.Create));
        Assert.False(result.Has(FilePermission.Delete));
        Assert.True(result.Has(FilePermission.Download));
    }

    [Fact]
    public void Administrator_BypassesRulesWhenSourceAllowsIt()
    {
        var source = NewSource(denyByDefault: true);
        var rules = new[]
        {
            Rule(AccessSubjectType.Everyone, null, AccessEffect.Deny, FilePermission.Full),
        };

        var result = PermissionResolver.Resolve(source, rules, Admin(), "/");

        Assert.True(result.IsAdministratorBypass);
        Assert.True(result.Has(FilePermission.Delete));
    }

    [Fact]
    public void Administrator_IsSubjectToRulesWhenBypassIsDisabled()
    {
        var source = NewSource(denyByDefault: true, administratorsBypassRules: false);

        var result = PermissionResolver.Resolve(source, [], Admin(), "/");

        Assert.False(result.IsAdministratorBypass);
        Assert.Equal(FilePermission.None, result.Effective);
    }

    [Fact]
    public void PrincipalCeiling_ClampsRuleGrants()
    {
        var source = NewSource();
        var rules = new[]
        {
            Rule(AccessSubjectType.User, "alice", AccessEffect.Allow, FilePermission.Editor),
        };

        var principal = User("alice") with { Ceiling = FilePermission.Full & ~FilePermission.Share };

        var result = PermissionResolver.Resolve(source, rules, principal, "/");

        Assert.False(result.Has(FilePermission.Share));
        Assert.True(result.Has(FilePermission.Delete));
    }

    [Fact]
    public void DisabledSource_GrantsNothingEvenToAdministrators()
    {
        var source = NewSource();
        source.IsDisabled = true;

        var result = PermissionResolver.Resolve(source, [], Admin(), "/");

        Assert.Equal(FilePermission.None, result.Effective);
    }

    [Fact]
    public void AnonymousPrincipal_DoesNotMatchAuthenticatedRules()
    {
        var source = NewSource(denyByDefault: true);
        var rules = new[]
        {
            Rule(AccessSubjectType.Authenticated, null, AccessEffect.Allow, FilePermission.ReadOnly),
        };

        var result = PermissionResolver.Resolve(source, rules, AccessPrincipal.Anonymous, "/");

        Assert.Equal(FilePermission.None, result.Effective);
    }

    [Fact]
    public void Trace_IsOnlyBuiltWhenRequested()
    {
        var source = NewSource();
        var rules = new[] { Rule(AccessSubjectType.Everyone, null, AccessEffect.Allow, FilePermission.ReadOnly) };

        Assert.Empty(PermissionResolver.Resolve(source, rules, User("alice"), "/").Trace);
        Assert.NotEmpty(PermissionResolver.Resolve(source, rules, User("alice"), "/", includeTrace: true).Trace);
    }

    // ---- Shared-folder and per-user home scenarios ----

    /// <summary>
    /// "A shared folder for ONLY me to write, but everyone else can ONLY read." Two rules on one
    /// source, without a source-wide read-only flag that would also lock out the owner.
    /// </summary>
    [Fact]
    public void Scenario_SharedFolderWritableByOwnerReadOnlyForEveryoneElse()
    {
        var source = NewSource(denyByDefault: true);
        var rules = new[]
        {
            Rule(AccessSubjectType.Authenticated, null, AccessEffect.Allow, FilePermission.ReadOnly),
            Rule(AccessSubjectType.User, "matt", AccessEffect.Allow, FilePermission.Editor),
        };

        var owner = PermissionResolver.Resolve(source, rules, User("matt"), "/holiday-photos");
        var family = PermissionResolver.Resolve(source, rules, User("jane"), "/holiday-photos");

        Assert.True(owner.Has(FilePermission.Create));
        Assert.True(owner.Has(FilePermission.Delete));

        Assert.True(family.Has(FilePermission.Browse));
        Assert.True(family.Has(FilePermission.Download));
        Assert.False(family.Has(FilePermission.Create));
        Assert.False(family.Has(FilePermission.Delete));
        Assert.False(family.Has(FilePermission.Modify));
    }

    /// <summary>
    /// "A separate folder for each user to both read and write." One Owner rule with a templated
    /// path covers every user, present and future, and nobody can see anyone else's folder.
    /// </summary>
    [Fact]
    public void Scenario_PerUserHomeDirectoriesFromASingleRule()
    {
        var source = NewSource(denyByDefault: true);
        var rules = new[]
        {
            Rule(
                AccessSubjectType.Owner,
                null,
                AccessEffect.Allow,
                FilePermission.Editor,
                path: "/" + Constants.PathTokens.UserName),
        };

        var ownFolder = PermissionResolver.Resolve(source, rules, User("matt"), "/matt/tax-returns");
        var othersFolder = PermissionResolver.Resolve(source, rules, User("matt"), "/jane");

        Assert.True(ownFolder.Has(FilePermission.Create));
        Assert.True(ownFolder.Has(FilePermission.Delete));
        Assert.Equal(FilePermission.None, othersFolder.Effective);
    }

    [Fact]
    public void OwnerRule_UsesDisplayNameForHomeFolderWhenSet()
    {
        var source = NewSource(denyByDefault: true);
        var rules = new[]
        {
            Rule(
                AccessSubjectType.Owner,
                null,
                AccessEffect.Allow,
                FilePermission.Editor,
                path: "/" + Constants.PathTokens.UserName),
        };

        var principal = new AccessPrincipal
        {
            UserId = "admin-id",
            UserName = "admin",
            DisplayName = "Matt",
            IsAuthenticated = true,
        };

        var atDisplayName = PermissionResolver.Resolve(source, rules, principal, "/Matt/notes.txt");
        var atLoginName = PermissionResolver.Resolve(source, rules, principal, "/admin/notes.txt");

        Assert.True(atDisplayName.Has(FilePermission.Create));
        Assert.Equal(FilePermission.None, atLoginName.Effective);
    }

    [Fact]
    public void Scenario_OwnerRuleIsNotMatchedByAnAnonymousVisitor()
    {
        var source = NewSource(denyByDefault: true);
        var rules = new[]
        {
            Rule(
                AccessSubjectType.Owner,
                null,
                AccessEffect.Allow,
                FilePermission.Editor,
                path: "/" + Constants.PathTokens.UserName),
        };

        // A literal "${username}" directory must not become reachable just because the token
        // could not be expanded.
        var result = PermissionResolver.Resolve(
            source,
            rules,
            AccessPrincipal.Anonymous,
            "/" + Constants.PathTokens.UserName);

        Assert.Equal(FilePermission.None, result.Effective);
    }

    [Fact]
    public void HasAnyBrowsableDescendant_KeepsDenyByDefaultSourcesNavigable()
    {
        var source = NewSource(denyByDefault: true);
        var rules = new[]
        {
            Rule(
                AccessSubjectType.Owner,
                null,
                AccessEffect.Allow,
                FilePermission.Editor,
                path: "/" + Constants.PathTokens.UserName),
        };

        var principal = User("matt");

        // Nothing is granted at the root, yet the source must still be listed or Matt can never
        // navigate down to the folder he does own.
        Assert.False(PermissionResolver.Resolve(source, rules, principal, "/").Has(FilePermission.Browse));
        Assert.True(PermissionResolver.HasAnyBrowsableDescendant(source, rules, principal, "/"));
    }

    [Fact]
    public void UserRule_MatchesUserNameWhenKeycloakIdDiffers()
    {
        var source = NewSource(denyByDefault: true, administratorsBypassRules: false);
        var rules = new[]
        {
            Rule(AccessSubjectType.User, "matt", AccessEffect.Allow, FilePermission.Editor),
        };

        var principal = new AccessPrincipal
        {
            UserId = "a1b2c3d4-from-keycloak",
            UserName = "matt",
            IsAuthenticated = true,
        };

        var result = PermissionResolver.Resolve(source, rules, principal, "/");

        Assert.True(result.Has(FilePermission.Create));
    }

    [Fact]
    public void ReadOnlySource_ClampsAdministratorBypass()
    {
        var source = NewSource(isReadOnly: true, administratorsBypassRules: true);

        var result = PermissionResolver.Resolve(source, [], Admin(), "/");

        Assert.False(result.Has(FilePermission.Create));
        Assert.True(result.IsClampedByReadOnlySource);
        Assert.True(result.Has(FilePermission.Browse));
    }

    [Fact]
    public void FilterForSubtree_KeepsAncestorsAndDescendantsOnly()
    {
        var rules = new[]
        {
            Rule(AccessSubjectType.Everyone, null, AccessEffect.Allow, FilePermission.Browse, path: "/"),
            Rule(AccessSubjectType.Everyone, null, AccessEffect.Allow, FilePermission.Browse, path: "/shared"),
            Rule(AccessSubjectType.Everyone, null, AccessEffect.Deny, FilePermission.Browse, path: "/shared/secret"),
            Rule(AccessSubjectType.Everyone, null, AccessEffect.Allow, FilePermission.Browse, path: "/elsewhere"),
        };

        var filtered = PermissionResolver.FilterForSubtree(rules, User("alice"), "/shared");

        Assert.Equal(3, filtered.Count);
        Assert.DoesNotContain(filtered, r => r.Path == "/elsewhere");
    }

    private static Source NewSource(
        bool denyByDefault = false,
        bool isReadOnly = false,
        bool administratorsBypassRules = true,
        FilePermission defaultPermissions = FilePermission.ReadOnly) =>
        new()
        {
            Id = 1,
            Name = "Test",
            Path = "/srv/test",
            DenyByDefault = denyByDefault,
            IsReadOnly = isReadOnly,
            AdministratorsBypassRules = administratorsBypassRules,
            DefaultPermissions = defaultPermissions,
        };

    private static AccessRule Rule(
        AccessSubjectType subjectType,
        string? subjectId,
        AccessEffect effect,
        FilePermission permissions,
        string path = "/",
        AccessPropagation propagation = AccessPropagation.ThisPathAndChildren,
        int priority = 0) =>
        new()
        {
            Id = nextRuleId++,
            SourceId = 1,
            Path = path,
            SubjectType = subjectType,
            SubjectId = subjectId,
            Effect = effect,
            Permissions = permissions,
            Propagation = propagation,
            Priority = priority,
        };

    private static AccessPrincipal User(string userId, params int[] groupIds) => new()
    {
        UserId = userId,
        UserName = userId,
        IsAuthenticated = true,
        IsAdministrator = false,
        GroupIds = groupIds.ToHashSet(),
    };

    private static AccessPrincipal Admin() => new()
    {
        UserId = "admin",
        UserName = "admin",
        IsAuthenticated = true,
        IsAdministrator = true,
        RoleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Constants.Roles.Administrator },
    };
}
