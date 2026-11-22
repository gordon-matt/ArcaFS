namespace ArcaFS.Services.Access;

/// <summary>
/// CRUD over a source's access rules. Callers must be an administrator, or hold
/// <see cref="FilePermission.ManagePermissions"/> at or above the rule's path — that second case
/// is what makes delegated administration of a subtree possible.
/// </summary>
public interface IAccessRuleService
{
    /// <summary>
    /// Lists a source's rules, shallowest path first. Subject ids are accompanied by display names.
    /// </summary>
    /// <param name="sourceId">Source whose rules are listed.</param>
    /// <param name="cancellationToken">Cancels the permission check and the lookup.</param>
    /// <returns>The rules, or forbidden when the caller cannot manage the source root.</returns>
    Task<Result<IReadOnlyList<AccessRuleDto>>> ListAsync(
        int sourceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a rule and drops the source's cached permissions. The caller must be able to
    /// manage permissions at the rule's path.
    /// </summary>
    /// <param name="request">Source, path, subject, effect, and the permissions the rule grants or denies.</param>
    /// <param name="cancellationToken">Cancels the validation and the insert.</param>
    /// <returns>The stored rule, or a failure when the request is invalid or the caller cannot manage that path.</returns>
    Task<Result<AccessRuleDto>> CreateAsync(
        CreateAccessRuleRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces a rule. Both the current path and the new path must be manageable, so a
    /// delegated administrator cannot move a rule out of the subtree they control.
    /// </summary>
    /// <param name="request">Rule id and the fields to store.</param>
    /// <param name="cancellationToken">Cancels the lookup and the update.</param>
    /// <returns>The stored rule, or a failure when it is missing, invalid, or outside the caller's control.</returns>
    Task<Result<AccessRuleDto>> UpdateAsync(
        UpdateAccessRuleRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a rule and drops the source's cached permissions.</summary>
    /// <param name="ruleId">Rule id.</param>
    /// <param name="cancellationToken">Cancels the lookup. The delete is not cancelled once it has started.</param>
    /// <returns>Success, or a failure when the rule is missing or the caller cannot manage its path.</returns>
    Task<Result> DeleteAsync(int ruleId, CancellationToken cancellationToken = default);

    /// <summary>Inserts a template's rules, optionally clearing the source's existing ACL first.</summary>
    /// <param name="sourceId">Source the template is applied to.</param>
    /// <param name="templateKey">Template key, such as the per-user home directories template.</param>
    /// <param name="subjectId">
    /// User id the template names, when it needs one. The shared read-only template requires this
    /// or a signed-in user.
    /// </param>
    /// <param name="replaceExisting">
    /// When <see langword="true"/>, the source's current rules are deleted before the template is inserted.
    /// </param>
    /// <param name="cancellationToken">Cancels the lookup and the writes.</param>
    /// <returns>
    /// Success, or a failure when the caller is not an administrator, the source or template is
    /// unknown, or a required subject is missing. The source's baseline settings are updated
    /// with the rules.
    /// </returns>
    Task<Result> ApplyTemplateAsync(
        int sourceId,
        string templateKey,
        string? subjectId,
        bool replaceExisting,
        CancellationToken cancellationToken = default);

    /// <summary>Templates that can be applied to this source, with the current user filled in where a template names one.</summary>
    /// <param name="sourceId">Source the templates would be applied to.</param>
    /// <param name="cancellationToken">Not observed.</param>
    /// <returns>The templates. This does not check that the caller may apply them.</returns>
    Task<IReadOnlyList<AccessTemplateDto>> GetTemplatesAsync(
        int sourceId,
        CancellationToken cancellationToken = default);
}
