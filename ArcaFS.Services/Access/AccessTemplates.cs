namespace ArcaFS.Services.Access;

/// <summary>
/// Ready-made rule sets for the layouts people actually ask for. Getting subject specificity and
/// propagation right by hand is the fiddly part of any ACL, so the rules editor offers these as
/// a starting point that can then be edited like any other rule.
/// </summary>
public static class AccessTemplates
{
    public const string PerUserHomeDirectories = "per-user-home";
    public const string SharedReadOnlyOwnerWrites = "shared-readonly-owner-writes";
    public const string PublicDropBox = "public-dropbox";
    public const string GroupWorkspace = "group-workspace";
    public const string LockedDown = "locked-down";

    /// <summary>
    /// Templates available for <paramref name="sourceId"/>. <paramref name="ownerUserId"/> fills
    /// in the "and I can still write" half of the shared-folder template.
    /// </summary>
    public static IReadOnlyList<AccessTemplateDto> ForSource(int sourceId, string? ownerUserId) =>
    [
        new(
            PerUserHomeDirectories,
            "Per-user home directories",
            "Each signed-in user gets full control of their own folder and cannot see anyone else's. "
                + "Set the source's user scope to ${username} so they land inside it.",
            [
                new CreateAccessRuleRequest
                {
                    SourceId = sourceId,
                    Path = "/" + Constants.PathTokens.UserName,
                    SubjectType = AccessSubjectType.Owner,
                    Effect = AccessEffect.Allow,
                    Permissions = FilePermission.Editor,
                    Propagation = AccessPropagation.ThisPathAndChildren,
                    Description = "Owner has full control of their own home directory",
                },
            ],
            RequiresDenyByDefault: true,
            SuggestedSourceDefault: FilePermission.None,
            ClearsHostReadOnly: true),

        new(
            SharedReadOnlyOwnerWrites,
            "Shared, read-only except for me",
            "Everyone signed in can browse and download. One nominated user can also add, change "
                + "and remove files. This is the layout that a source-wide read-only switch cannot express.",
            ownerUserId is null
                ?
                [
                    new CreateAccessRuleRequest
                    {
                        SourceId = sourceId,
                        SubjectType = AccessSubjectType.Authenticated,
                        Effect = AccessEffect.Allow,
                        Permissions = FilePermission.ReadOnly,
                        Description = "Everyone signed in may look but not touch",
                    },
                ]
                :
                [
                    new CreateAccessRuleRequest
                    {
                        SourceId = sourceId,
                        SubjectType = AccessSubjectType.Authenticated,
                        Effect = AccessEffect.Allow,
                        Permissions = FilePermission.ReadOnly,
                        Description = "Everyone signed in may look but not touch",
                    },
                    new CreateAccessRuleRequest
                    {
                        SourceId = sourceId,
                        SubjectType = AccessSubjectType.User,
                        SubjectId = ownerUserId,
                        Effect = AccessEffect.Allow,
                        Permissions = FilePermission.Editor,
                        Description = "Nominated owner may write",
                    },
                ],
            RequiresDenyByDefault: false,
            SuggestedSourceDefault: FilePermission.None,
            ClearsHostReadOnly: true),

        new(
            PublicDropBox,
            "Drop box",
            "Anyone signed in may upload into one folder but cannot browse, read or delete what is "
                + "already there. Useful for collecting submissions.",
            [
                new CreateAccessRuleRequest
                {
                    SourceId = sourceId,
                    Path = "/dropbox",
                    SubjectType = AccessSubjectType.Authenticated,
                    Effect = AccessEffect.Allow,
                    Permissions = FilePermission.Browse | FilePermission.Create,
                    Propagation = AccessPropagation.ThisPathAndChildren,
                    Description = "Upload-only access to the drop box",
                },
                new CreateAccessRuleRequest
                {
                    SourceId = sourceId,
                    Path = "/dropbox",
                    SubjectType = AccessSubjectType.Authenticated,
                    Effect = AccessEffect.Deny,
                    Permissions = FilePermission.Read | FilePermission.Download,
                    Propagation = AccessPropagation.ChildrenOnly,
                    Description = "Submissions are not readable by other submitters",
                },
            ],
            RequiresDenyByDefault: true,
            SuggestedSourceDefault: FilePermission.None,
            ClearsHostReadOnly: true),

        new(
            GroupWorkspace,
            "Group workspace",
            "One access group gets full control of a folder; nobody else can see it. Pick the group "
                + "after applying the template.",
            [
                new CreateAccessRuleRequest
                {
                    SourceId = sourceId,
                    Path = "/workspace",
                    SubjectType = AccessSubjectType.Group,
                    Effect = AccessEffect.Allow,
                    Permissions = FilePermission.Editor,
                    Propagation = AccessPropagation.ThisPathAndChildren,
                    Description = "Group members collaborate in the workspace",
                },
            ],
            RequiresDenyByDefault: true,
            SuggestedSourceDefault: FilePermission.None,
            ClearsHostReadOnly: true),

        new(
            LockedDown,
            "Administrators only",
            "Removes every rule and denies by default, so only administrators (who bypass rules) "
                + "can reach the source.",
            [],
            RequiresDenyByDefault: true,
            SuggestedSourceDefault: FilePermission.None),
    ];

    /// <summary>
    /// Applies the template's source-level defaults. The rules alone are not enough: a host-level
    /// read-only clamp would still strip write bits from the nominated owner, and a leftover
    /// default of Full would leave everyone else able to write.
    /// </summary>
    public static bool ApplySourceSettings(Source source, AccessTemplateDto template)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(template);

        bool changed = false;

        if (template.RequiresDenyByDefault && !source.DenyByDefault)
        {
            source.DenyByDefault = true;
            changed = true;
        }

        if (template.SuggestedSourceDefault is { } suggested
            && source.DefaultPermissions != suggested)
        {
            source.DefaultPermissions = suggested;
            changed = true;
        }

        if (template.ClearsHostReadOnly && source.IsReadOnly)
        {
            source.IsReadOnly = false;
            changed = true;
        }

        if (template.Key == PerUserHomeDirectories
            && string.IsNullOrWhiteSpace(source.DefaultUserScope))
        {
            source.DefaultUserScope = "/" + Constants.PathTokens.UserName;
            source.CreateUserDirectory = true;
            changed = true;
        }

        return changed;
    }
}