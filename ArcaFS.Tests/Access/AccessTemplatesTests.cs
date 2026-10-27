namespace ArcaFS.Tests.Access;

public class AccessTemplatesTests
{
    [Fact]
    public void SharedReadOnlyTemplate_ClearsHostReadOnlyAndSetsDefaultNone()
    {
        var source = new Source
        {
            Name = "Shared",
            Path = "/data/shared",
            IsReadOnly = true,
            DefaultPermissions = FilePermission.ReadOnly,
            DenyByDefault = false,
        };

        var template = AccessTemplates
            .ForSource(1, "owner-id")
            .First(t => t.Key == AccessTemplates.SharedReadOnlyOwnerWrites);

        Assert.True(AccessTemplates.ApplySourceSettings(source, template));
        Assert.False(source.IsReadOnly);
        Assert.Equal(FilePermission.None, source.DefaultPermissions);
        Assert.False(source.DenyByDefault);
    }

    [Fact]
    public void LockedDownTemplate_DoesNotClearHostReadOnly()
    {
        var source = new Source
        {
            Name = "Archive",
            Path = "/data/archive",
            IsReadOnly = true,
            DefaultPermissions = FilePermission.ReadOnly,
        };

        var template = AccessTemplates
            .ForSource(1, null)
            .First(t => t.Key == AccessTemplates.LockedDown);

        AccessTemplates.ApplySourceSettings(source, template);

        Assert.True(source.IsReadOnly);
        Assert.True(source.DenyByDefault);
        Assert.Equal(FilePermission.None, source.DefaultPermissions);
    }
}