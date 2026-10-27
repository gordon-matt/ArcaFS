namespace ArcaFS.Services.Access;

public sealed class AccessGroupService(
    ILogger<AccessGroupService> logger,
    IUserContextService userContext,
    IUserInfoService userInfoService,
    IRepository<AccessGroup> groupRepository,
    IRepository<AccessGroupMember> memberRepository,
    IRepository<AccessRule> ruleRepository) : IAccessGroupService
{
    public async Task<Result<IReadOnlyList<AccessGroupDto>>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result<IReadOnlyList<AccessGroupDto>>.Forbidden();
        }

        var groups = await groupRepository.FindAsync(new SearchOptions<AccessGroup>
        {
            Include = q => q.Include(g => g.Members),
            OrderBy = q => q.OrderBy(g => g.Name),
            CancellationToken = cancellationToken,
        });

        var allUserIds = groups.SelectMany(g => g.Members.Select(m => m.UserId)).Distinct(StringComparer.Ordinal);
        var users = await userInfoService.GetUserInfoAsync(allUserIds, cancellationToken);

        IReadOnlyList<AccessGroupDto> dtos = groups
            .Select(g => new AccessGroupDto(
                g.Id,
                g.Name,
                g.Description,
                g.IsExternallyManaged,
                g.Members.Count,
                g.Members
                    .Select(m =>
                    {
                        var info = users.GetValueOrDefault(m.UserId);
                        return new AccessGroupMemberDto(m.UserId, info?.UserName, info?.DisplayName);
                    })
                    .OrderBy(m => m.UserName ?? m.UserId, StringComparer.OrdinalIgnoreCase)
                    .ToList()))
            .ToList();

        return Result<IReadOnlyList<AccessGroupDto>>.Success(dtos);
    }

    public async Task<Result<AccessGroupDto>> SaveAsync(
        SaveAccessGroupRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result<AccessGroupDto>.Forbidden();
        }

        string name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return Result<AccessGroupDto>.Invalid(new ValidationError(nameof(request.Name), "Name is required."));
        }

        var clash = await groupRepository.FindOneAsync(new SearchOptions<AccessGroup>
        {
            Query = g => g.Id != request.Id && g.Name == name,
            CancellationToken = cancellationToken,
        });

        if (clash is not null)
        {
            return Result<AccessGroupDto>.Conflict($"A group named '{name}' already exists.");
        }

        AccessGroup group;
        if (request.Id == 0)
        {
            group = await groupRepository.InsertAsync(new AccessGroup
            {
                Name = name,
                Description = request.Description?.Trim(),
            });
        }
        else
        {
            var existing = await groupRepository.FindOneAsync(new SearchOptions<AccessGroup>
            {
                Query = g => g.Id == request.Id,
                CancellationToken = cancellationToken,
            });

            if (existing is null)
            {
                return Result<AccessGroupDto>.NotFound($"Group {request.Id} was not found.");
            }

            if (existing.IsExternallyManaged)
            {
                return Result<AccessGroupDto>.Invalid(new ValidationError(
                    nameof(request.MemberUserIds),
                    "This group is synchronised from the identity provider, so its membership cannot be edited here."));
            }

            existing.Name = name;
            existing.Description = request.Description?.Trim();
            await groupRepository.UpdateAsync(existing);
            group = existing;
        }

        await ReplaceMembersAsync(group.Id, request.MemberUserIds, cancellationToken);

        logger.LogInformation(
            "Access group {GroupId} '{Name}' saved with {MemberCount} member(s)",
            group.Id,
            group.Name,
            request.MemberUserIds.Count);

        var listed = await ListAsync(cancellationToken);
        var dto = listed.IsSuccess ? listed.Value.FirstOrDefault(g => g.Id == group.Id) : null;

        return dto is null
            ? Result<AccessGroupDto>.Success(new AccessGroupDto(group.Id, group.Name, group.Description, false, 0, []))
            : Result<AccessGroupDto>.Success(dto);
    }

    public async Task<Result> DeleteAsync(int groupId, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var group = await groupRepository.FindOneAsync(new SearchOptions<AccessGroup>
        {
            Query = g => g.Id == groupId,
            CancellationToken = cancellationToken,
        });

        if (group is null)
        {
            return Result.NotFound($"Group {groupId} was not found.");
        }

        // Rules referencing a deleted group would silently stop matching, which looks like the
        // permission system misbehaving. Make the admin deal with them first.
        string groupIdText = groupId.ToString();
        int ruleCount = await ruleRepository.CountAsync(r =>
            r.SubjectType == AccessSubjectType.Group && r.SubjectId == groupIdText);

        if (ruleCount > 0)
        {
            return Result.Conflict(
                $"'{group.Name}' is used by {ruleCount} access rule(s). Remove those rules first.");
        }

        await groupRepository.DeleteAsync(group);
        logger.LogInformation("Access group {GroupId} '{Name}' deleted", groupId, group.Name);

        return Result.Success();
    }

    public async Task<Result> SyncExternalGroupsAsync(
        string userId,
        IEnumerable<string> groupNames,
        CancellationToken cancellationToken = default)
    {
        var wanted = groupNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim().TrimStart('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var allGroups = (await groupRepository.FindAsync(new SearchOptions<AccessGroup>
        {
            CancellationToken = cancellationToken,
        })).ToList();

        var byName = allGroups.ToDictionary(g => g.Name, StringComparer.OrdinalIgnoreCase);

        foreach (string name in wanted.Where(n => !byName.ContainsKey(n)))
        {
            var created = await groupRepository.InsertAsync(new AccessGroup
            {
                Name = name,
                Description = "Synchronised from the identity provider",
                IsExternallyManaged = true,
            });

            byName[name] = created;
            logger.LogInformation("Created externally-managed access group '{Name}'", name);
        }

        var wantedIds = wanted.Select(n => byName[n].Id).ToHashSet();
        var externalGroupIds = byName.Values.Where(g => g.IsExternallyManaged).Select(g => g.Id).ToHashSet();

        var current = (await memberRepository.FindAsync(new SearchOptions<AccessGroupMember>
        {
            Query = m => m.UserId == userId,
            CancellationToken = cancellationToken,
        })).ToList();

        // Only externally-managed memberships are reconciled; a locally-assigned group survives
        // a sign-in even if the IdP has never heard of it.
        var toRemove = current
            .Where(m => externalGroupIds.Contains(m.AccessGroupId) && !wantedIds.Contains(m.AccessGroupId))
            .ToList();

        if (toRemove.Count > 0)
        {
            await memberRepository.DeleteAsync(toRemove);
        }

        var currentIds = current.Select(m => m.AccessGroupId).ToHashSet();
        var toAdd = wantedIds
            .Where(id => !currentIds.Contains(id))
            .Select(id => new AccessGroupMember { AccessGroupId = id, UserId = userId })
            .ToList();

        if (toAdd.Count > 0)
        {
            await memberRepository.InsertAsync(toAdd);
        }

        return Result.Success();
    }

    private async Task ReplaceMembersAsync(
        int groupId,
        IReadOnlyList<string> memberUserIds,
        CancellationToken cancellationToken)
    {
        var existing = (await memberRepository.FindAsync(new SearchOptions<AccessGroupMember>
        {
            Query = m => m.AccessGroupId == groupId,
            CancellationToken = cancellationToken,
        })).ToList();

        var wanted = memberUserIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        var toRemove = existing.Where(m => !wanted.Contains(m.UserId)).ToList();
        if (toRemove.Count > 0)
        {
            await memberRepository.DeleteAsync(toRemove);
        }

        var currentIds = existing.Select(m => m.UserId).ToHashSet(StringComparer.Ordinal);
        var toAdd = wanted
            .Where(id => !currentIds.Contains(id))
            .Select(id => new AccessGroupMember { AccessGroupId = groupId, UserId = id })
            .ToList();

        if (toAdd.Count > 0)
        {
            await memberRepository.InsertAsync(toAdd);
        }
    }
}