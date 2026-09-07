namespace ArcaFS.Models;

/// <summary>One access rule, with the subject resolved to a display name for the rules editor.</summary>
public record AccessRuleDto(
    int Id,
    int SourceId,
    string Path,
    AccessSubjectType SubjectType,
    string? SubjectId,
    string? SubjectDisplayName,
    AccessEffect Effect,
    FilePermission Permissions,
    AccessPropagation Propagation,
    int Priority,
    string? Description,
    DateTime CreatedAt);

public record CreateAccessRuleRequest
{
    public int SourceId { get; init; }

    /// <summary>May contain <c>${username}</c> / <c>${userid}</c>.</summary>
    public string Path { get; init; } = Constants.RootPath;

    public AccessSubjectType SubjectType { get; init; } = AccessSubjectType.User;

    public string? SubjectId { get; init; }

    public AccessEffect Effect { get; init; } = AccessEffect.Allow;

    public FilePermission Permissions { get; init; } = FilePermission.ReadOnly;

    public AccessPropagation Propagation { get; init; } = AccessPropagation.ThisPathAndChildren;

    public int Priority { get; init; }

    public string? Description { get; init; }
}

public record UpdateAccessRuleRequest : CreateAccessRuleRequest
{
    public int Id { get; init; }
}

/// <summary>
/// The resolver's answer for one (source, path, principal) triple, plus the reasoning. The trace
/// is what makes this permission model debuggable — an admin can see exactly which rule granted
/// or removed a bit instead of guessing.
/// </summary>
public record EffectivePermissionsDto(
    int SourceId,
    string Path,
    string? UserId,
    string? UserName,
    FilePermission Effective,
    bool IsAdministratorBypass,
    bool IsClampedByReadOnlySource,
    IReadOnlyList<PermissionTraceEntryDto> Trace);

/// <summary>
/// One step in the resolution. <paramref name="Before"/> and <paramref name="After"/> bracket the
/// change so the UI can highlight which bits this step actually moved.
/// </summary>
public record PermissionTraceEntryDto(
    string Description,
    int? RuleId,
    string? RulePath,
    AccessSubjectType? SubjectType,
    string? SubjectDisplayName,
    AccessEffect? Effect,
    FilePermission RulePermissions,
    FilePermission Before,
    FilePermission After);

/// <summary>Request behind the "what can this user do here?" simulator on the access admin page.</summary>
public record EffectivePermissionsRequest
{
    public int SourceId { get; init; }

    public string Path { get; init; } = Constants.RootPath;

    /// <summary>Null means evaluate as an anonymous share visitor.</summary>
    public string? UserId { get; init; }
}

public record AccessGroupDto(
    int Id,
    string Name,
    string? Description,
    bool IsExternallyManaged,
    int MemberCount,
    IReadOnlyList<AccessGroupMemberDto> Members);

public record AccessGroupMemberDto(string UserId, string? UserName, string? DisplayName);

public record SaveAccessGroupRequest
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public IReadOnlyList<string> MemberUserIds { get; init; } = [];
}

/// <summary>
/// A ready-made rule set for a common intent, offered in the rules editor so an admin doesn't
/// have to reason about propagation and subject specificity to get a standard layout.
/// </summary>
public record AccessTemplateDto(
    string Key,
    string Name,
    string Description,
    IReadOnlyList<CreateAccessRuleRequest> Rules,
    bool RequiresDenyByDefault,
    FilePermission? SuggestedSourceDefault);
