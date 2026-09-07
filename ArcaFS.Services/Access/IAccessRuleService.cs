namespace ArcaFS.Services.Access;

/// <summary>
/// CRUD over a source's access rules. Callers must be an administrator, or hold
/// <see cref="FilePermission.ManagePermissions"/> at or above the rule's path — that second case
/// is what makes delegated administration of a subtree possible.
/// </summary>
public interface IAccessRuleService
{
    Task<Result<IReadOnlyList<AccessRuleDto>>> ListAsync(
        int sourceId,
        CancellationToken cancellationToken = default);

    Task<Result<AccessRuleDto>> CreateAsync(
        CreateAccessRuleRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<AccessRuleDto>> UpdateAsync(
        UpdateAccessRuleRequest request,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(int ruleId, CancellationToken cancellationToken = default);

    /// <summary>Inserts a template's rules, optionally clearing the source's existing ACL first.</summary>
    Task<Result> ApplyTemplateAsync(
        int sourceId,
        string templateKey,
        string? subjectId,
        bool replaceExisting,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AccessTemplateDto>> GetTemplatesAsync(
        int sourceId,
        CancellationToken cancellationToken = default);
}
