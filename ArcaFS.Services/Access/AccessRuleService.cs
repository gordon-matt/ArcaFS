namespace ArcaFS.Services.Access;

public sealed class AccessRuleService(
    ILogger<AccessRuleService> logger,
    IUserContextService userContext,
    IUserInfoService userInfoService,
    IPermissionService permissions,
    IRepository<AccessRule> ruleRepository,
    IRepository<Source> sourceRepository,
    IRepository<AccessGroup> groupRepository) : IAccessRuleService
{
    public async Task<Result<IReadOnlyList<AccessRuleDto>>> ListAsync(
        int sourceId,
        CancellationToken cancellationToken = default)
    {
        var authorized = await CanManageAsync(sourceId, Constants.RootPath, cancellationToken);
        if (!authorized)
        {
            return Result<IReadOnlyList<AccessRuleDto>>.Forbidden();
        }

        var rules = await ruleRepository.FindAsync(new SearchOptions<AccessRule>
        {
            Query = r => r.SourceId == sourceId,
            CancellationToken = cancellationToken,
        });

        var ordered = rules
            .OrderBy(r => VirtualPath.Depth(r.Path))
            .ThenBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => (int)r.SubjectType)
            .ThenBy(r => r.Priority)
            .ToList();

        var displayNames = await ResolveSubjectDisplayNamesAsync(ordered, cancellationToken);

        IReadOnlyList<AccessRuleDto> dtos = ordered
            .Select(r => Map(r, displayNames))
            .ToList();

        return Result<IReadOnlyList<AccessRuleDto>>.Success(dtos);
    }

    public async Task<Result<AccessRuleDto>> CreateAsync(
        CreateAccessRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = await ValidateAsync(request, cancellationToken);
        if (!validation.IsSuccess)
        {
            return Result<AccessRuleDto>.Invalid(validation.ValidationErrors);
        }

        if (!await CanManageAsync(request.SourceId, request.Path, cancellationToken))
        {
            return Result<AccessRuleDto>.Forbidden();
        }

        var rule = await ruleRepository.InsertAsync(new AccessRule
        {
            SourceId = request.SourceId,
            Path = NormalizeRulePath(request.Path),
            SubjectType = request.SubjectType,
            SubjectId = NormalizeSubjectId(request.SubjectType, request.SubjectId),
            Effect = request.Effect,
            Permissions = request.Permissions,
            Propagation = request.Propagation,
            Priority = request.Priority,
            Description = request.Description?.Trim(),
            CreatedByUserId = userContext.GetCurrentUserId(),
        });

        permissions.InvalidateSource(request.SourceId);

        logger.LogInformation(
            "Access rule {RuleId} created on source {SourceId}: {Effect} {Permissions} for {SubjectType} at {Path}",
            rule.Id,
            rule.SourceId,
            rule.Effect,
            rule.Permissions,
            rule.SubjectType,
            rule.Path);

        var names = await ResolveSubjectDisplayNamesAsync([rule], cancellationToken);
        return Result<AccessRuleDto>.Success(Map(rule, names));
    }

    public async Task<Result<AccessRuleDto>> UpdateAsync(
        UpdateAccessRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        var rule = await ruleRepository.FindOneAsync(new SearchOptions<AccessRule>
        {
            Query = r => r.Id == request.Id,
            CancellationToken = cancellationToken,
        });

        if (rule is null)
        {
            return Result<AccessRuleDto>.NotFound($"Access rule {request.Id} was not found.");
        }

        var validation = await ValidateAsync(request, cancellationToken);
        if (!validation.IsSuccess)
        {
            return Result<AccessRuleDto>.Invalid(validation.ValidationErrors);
        }

        // Both the old and the new path must be manageable, so a delegated administrator can't
        // move a rule out of the subtree they control.
        if (!await CanManageAsync(rule.SourceId, rule.Path, cancellationToken)
            || !await CanManageAsync(request.SourceId, request.Path, cancellationToken))
        {
            return Result<AccessRuleDto>.Forbidden();
        }

        int previousSourceId = rule.SourceId;

        rule.SourceId = request.SourceId;
        rule.Path = NormalizeRulePath(request.Path);
        rule.SubjectType = request.SubjectType;
        rule.SubjectId = NormalizeSubjectId(request.SubjectType, request.SubjectId);
        rule.Effect = request.Effect;
        rule.Permissions = request.Permissions;
        rule.Propagation = request.Propagation;
        rule.Priority = request.Priority;
        rule.Description = request.Description?.Trim();

        await ruleRepository.UpdateAsync(rule);

        permissions.InvalidateSource(previousSourceId);
        permissions.InvalidateSource(rule.SourceId);

        var names = await ResolveSubjectDisplayNamesAsync([rule], cancellationToken);
        return Result<AccessRuleDto>.Success(Map(rule, names));
    }

    public async Task<Result> DeleteAsync(int ruleId, CancellationToken cancellationToken = default)
    {
        var rule = await ruleRepository.FindOneAsync(new SearchOptions<AccessRule>
        {
            Query = r => r.Id == ruleId,
            CancellationToken = cancellationToken,
        });

        if (rule is null)
        {
            return Result.NotFound($"Access rule {ruleId} was not found.");
        }

        if (!await CanManageAsync(rule.SourceId, rule.Path, cancellationToken))
        {
            return Result.Forbidden();
        }

        await ruleRepository.DeleteAsync(rule);
        permissions.InvalidateSource(rule.SourceId);

        logger.LogInformation("Access rule {RuleId} deleted from source {SourceId}", ruleId, rule.SourceId);
        return Result.Success();
    }

    public async Task<Result> ApplyTemplateAsync(
        int sourceId,
        string templateKey,
        string? subjectId,
        bool replaceExisting,
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var source = await sourceRepository.FindOneAsync(new SearchOptions<Source>
        {
            Query = s => s.Id == sourceId,
            CancellationToken = cancellationToken,
        });

        if (source is null)
        {
            return Result.NotFound($"Source {sourceId} was not found.");
        }

        var template = AccessTemplates
            .ForSource(sourceId, subjectId)
            .FirstOrDefault(t => t.Key == templateKey);

        if (template is null)
        {
            return Result.Invalid(new ValidationError(nameof(templateKey), $"Unknown template '{templateKey}'."));
        }

        if (replaceExisting)
        {
            var existing = await ruleRepository.FindAsync(new SearchOptions<AccessRule>
            {
                Query = r => r.SourceId == sourceId,
                CancellationToken = cancellationToken,
            });

            if (existing.Count > 0)
            {
                await ruleRepository.DeleteAsync(existing);
            }
        }

        string? currentUserId = userContext.GetCurrentUserId();
        var toInsert = template.Rules
            .Select(r => new AccessRule
            {
                SourceId = sourceId,
                Path = NormalizeRulePath(r.Path),
                SubjectType = r.SubjectType,
                SubjectId = NormalizeSubjectId(r.SubjectType, r.SubjectId ?? subjectId),
                Effect = r.Effect,
                Permissions = r.Permissions,
                Propagation = r.Propagation,
                Priority = r.Priority,
                Description = r.Description,
                CreatedByUserId = currentUserId,
            })
            .ToList();

        if (toInsert.Count > 0)
        {
            await ruleRepository.InsertAsync(toInsert);
        }

        // The template's rules only make sense alongside its baseline, so apply that too rather
        // than leaving the admin with a half-configured source.
        if (template.RequiresDenyByDefault && !source.DenyByDefault)
        {
            source.DenyByDefault = true;
            await sourceRepository.UpdateAsync(source);
        }

        if (templateKey == AccessTemplates.PerUserHomeDirectories
            && string.IsNullOrWhiteSpace(source.DefaultUserScope))
        {
            source.DefaultUserScope = "/" + Constants.PathTokens.UserName;
            source.CreateUserDirectory = true;
            await sourceRepository.UpdateAsync(source);
        }

        permissions.InvalidateSource(sourceId);

        logger.LogInformation(
            "Applied access template '{Template}' to source {SourceId} ({RuleCount} rule(s), replace={Replace})",
            templateKey,
            sourceId,
            toInsert.Count,
            replaceExisting);

        return Result.Success();
    }

    public Task<IReadOnlyList<AccessTemplateDto>> GetTemplatesAsync(
        int sourceId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(AccessTemplates.ForSource(sourceId, userContext.GetCurrentUserId()));

    /// <summary>
    /// Administrators may manage any rule. Everyone else needs
    /// <see cref="FilePermission.ManagePermissions"/> at the rule's path, which they can only
    /// have been granted by a rule an administrator wrote.
    /// </summary>
    private async Task<bool> CanManageAsync(int sourceId, string path, CancellationToken cancellationToken)
    {
        if (userContext.IsAdministrator())
        {
            return true;
        }

        var effective = await permissions.GetPermissionsAsync(sourceId, path, cancellationToken);
        return effective.Has(FilePermission.ManagePermissions);
    }

    private async Task<Result> ValidateAsync(CreateAccessRuleRequest request, CancellationToken cancellationToken)
    {
        var errors = new List<ValidationError>();

        if (request.Permissions == FilePermission.None)
        {
            errors.Add(new ValidationError(
                nameof(request.Permissions),
                "Select at least one permission — a rule that names none has no effect."));
        }

        bool sourceExists = await sourceRepository.ExistsAsync(s => s.Id == request.SourceId);
        if (!sourceExists)
        {
            errors.Add(new ValidationError(nameof(request.SourceId), "Select a source."));
        }

        switch (request.SubjectType)
        {
            case AccessSubjectType.User when string.IsNullOrWhiteSpace(request.SubjectId):
                errors.Add(new ValidationError(nameof(request.SubjectId), "Select a user."));
                break;

            case AccessSubjectType.Role when string.IsNullOrWhiteSpace(request.SubjectId):
                errors.Add(new ValidationError(nameof(request.SubjectId), "Select a role."));
                break;

            case AccessSubjectType.Group:
                if (!int.TryParse(request.SubjectId, out int groupId))
                {
                    errors.Add(new ValidationError(nameof(request.SubjectId), "Select a group."));
                }
                else if (!await groupRepository.ExistsAsync(g => g.Id == groupId))
                {
                    errors.Add(new ValidationError(nameof(request.SubjectId), "That group no longer exists."));
                }

                break;

            case AccessSubjectType.Owner when !VirtualPath.HasTokens(request.Path):
                // Without a token an Owner rule is indistinguishable from Authenticated, and
                // would silently hand every user access to one shared directory.
                errors.Add(new ValidationError(
                    nameof(request.Path),
                    $"An owner rule needs {Constants.PathTokens.UserName} or {Constants.PathTokens.UserId} "
                        + "in its path so it applies to each user's own folder."));
                break;
        }

        return errors.Count > 0 ? Result.Invalid(errors) : Result.Success();
    }

    /// <summary>
    /// Normalizes a rule path while leaving <c>${...}</c> tokens intact — they are expanded per
    /// principal at evaluation time, not stored expanded.
    /// </summary>
    private static string NormalizeRulePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Constants.RootPath;
        }

        string trimmed = path.Trim().Replace('\\', Constants.PathSeparator);
        if (!trimmed.StartsWith(Constants.PathSeparator))
        {
            trimmed = Constants.PathSeparator + trimmed;
        }

        while (trimmed.Length > 1 && trimmed.EndsWith(Constants.PathSeparator))
        {
            trimmed = trimmed[..^1];
        }

        return trimmed.Replace("//", "/");
    }

    private static string? NormalizeSubjectId(AccessSubjectType subjectType, string? subjectId) =>
        subjectType is AccessSubjectType.Everyone
            or AccessSubjectType.Anonymous
            or AccessSubjectType.Authenticated
            or AccessSubjectType.Owner
            ? null
            : subjectId?.Trim();

    private async Task<Dictionary<string, string>> ResolveSubjectDisplayNamesAsync(
        IReadOnlyList<AccessRule> rules,
        CancellationToken cancellationToken)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);

        var userIds = rules
            .Where(r => r.SubjectType == AccessSubjectType.User && !string.IsNullOrEmpty(r.SubjectId))
            .Select(r => r.SubjectId!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (userIds.Count > 0)
        {
            var users = await userInfoService.GetUserInfoAsync(userIds, cancellationToken);
            foreach (var (id, info) in users)
            {
                names[id] = info.Label;
            }
        }

        var groupIds = rules
            .Where(r => r.SubjectType == AccessSubjectType.Group && !string.IsNullOrEmpty(r.SubjectId))
            .Select(r => r.SubjectId!)
            .Distinct(StringComparer.Ordinal)
            .Select(s => int.TryParse(s, out int id) ? id : (int?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();

        if (groupIds.Count > 0)
        {
            var groups = await groupRepository.FindAsync(new SearchOptions<AccessGroup>
            {
                Query = g => groupIds.Contains(g.Id),
                CancellationToken = cancellationToken,
            });

            foreach (var group in groups)
            {
                names[group.Id.ToString()] = group.Name;
            }
        }

        return names;
    }

    private static AccessRuleDto Map(AccessRule rule, IReadOnlyDictionary<string, string> displayNames) =>
        new(
            rule.Id,
            rule.SourceId,
            rule.Path,
            rule.SubjectType,
            rule.SubjectId,
            rule.SubjectId is not null ? displayNames.GetValueOrDefault(rule.SubjectId, rule.SubjectId) : null,
            rule.Effect,
            rule.Permissions,
            rule.Propagation,
            rule.Priority,
            rule.Description,
            rule.CreatedAt);
}
