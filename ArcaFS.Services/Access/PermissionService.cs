using Microsoft.Extensions.Caching.Memory;

namespace ArcaFS.Services.Access;

/// <summary>
/// Default <see cref="IPermissionService"/>. Loads sources and rules through
/// <see cref="IMemoryCache"/> because a single directory listing resolves permissions once per
/// child, and hitting the database for each would be untenable on a folder with a thousand files.
/// The cache is invalidated explicitly whenever an ACL or source changes.
/// </summary>
public sealed class PermissionService(
    ILogger<PermissionService> logger,
    IUserContextService userContext,
    IUserInfoService userInfoService,
    IMemoryCache cache,
    IRepository<Source> sourceRepository,
    IRepository<AccessRule> accessRuleRepository,
    IRepository<AccessGroupMember> groupMemberRepository,
    IDbContextFactory dbContextFactory) : IPermissionService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    /// <summary>Memoised for the lifetime of the scope — one principal per request.</summary>
    private AccessPrincipal? currentPrincipal;

    /// <summary>Memoised so the sidebar and home page share one load instead of racing.</summary>
    private IReadOnlyList<SourceSummaryDto>? accessibleSources;

    private readonly SemaphoreSlim principalLock = new(1, 1);
    private readonly SemaphoreSlim accessibleSourcesLock = new(1, 1);

    public async Task<AccessPrincipal> GetCurrentPrincipalAsync(CancellationToken cancellationToken = default)
    {
        if (currentPrincipal is not null)
        {
            return currentPrincipal;
        }

        await principalLock.WaitAsync(cancellationToken);

        try
        {
            if (currentPrincipal is not null)
            {
                return currentPrincipal;
            }

            return await LoadCurrentPrincipalAsync(cancellationToken);
        }
        finally
        {
            principalLock.Release();
        }
    }

    private async Task<AccessPrincipal> LoadCurrentPrincipalAsync(CancellationToken cancellationToken)
    {
        string? userId = userContext.GetCurrentUserId();
        if (!userContext.IsAuthenticated() || string.IsNullOrEmpty(userId))
        {
            return currentPrincipal = AccessPrincipal.Anonymous;
        }

        var groupIds = (await groupMemberRepository.FindAsync(new SearchOptions<AccessGroupMember>
        {
            Query = m => m.UserId == userId,
            CancellationToken = cancellationToken,
        })).Select(m => m.AccessGroupId).ToHashSet();

        // The global share switch lives on the Identity row rather than in the ACL so it can be
        // revoked in one place; it becomes a ceiling the rules can't lift.
        var ceiling = FilePermission.Full;
        var user = await FindLocalUserAsync(userId, cancellationToken);

        if (user is not null && !user.CanCreateShares)
        {
            ceiling &= ~FilePermission.Share;
        }

        return currentPrincipal = new AccessPrincipal
        {
            UserId = userId,
            UserName = user?.UserName ?? userContext.GetCurrentUserName(),
            DisplayName = user?.DisplayName,
            IsAuthenticated = true,
            IsAdministrator = userContext.IsAdministrator(),
            RoleNames = userContext.GetRoleNames().ToHashSet(StringComparer.OrdinalIgnoreCase),
            GroupIds = groupIds,
            Ceiling = ceiling,
        };
    }

    public async Task<FilePermission> GetPermissionsAsync(
        int sourceId,
        string virtualPath,
        CancellationToken cancellationToken = default)
    {
        var source = await GetSourceAsync(sourceId, cancellationToken);
        if (source is null)
        {
            return FilePermission.None;
        }

        var principal = await GetCurrentPrincipalAsync(cancellationToken);
        var rules = await GetRulesAsync(sourceId, cancellationToken);
        string scope = ResolveScope(source, principal);
        string absolute = VirtualPath.Combine(scope, VirtualPath.Normalize(virtualPath).TrimStart('/'));

        return PermissionResolver.Resolve(source, rules, principal, absolute).Effective;
    }

    public async Task<Result<ResolvedPath>> RequireAsync(
        int sourceId,
        string virtualPath,
        FilePermission required,
        CancellationToken cancellationToken = default)
    {
        var principal = await GetCurrentPrincipalAsync(cancellationToken);
        return await RequireAsync(sourceId, virtualPath, required, principal, cancellationToken);
    }

    public async Task<Result<ResolvedPath>> RequireAsync(
        int sourceId,
        string virtualPath,
        FilePermission required,
        AccessPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var source = await GetSourceAsync(sourceId, cancellationToken);
        if (source is null || source.IsDisabled)
        {
            // Deliberately "not found" rather than "forbidden": a caller who can't see a source
            // shouldn't be able to confirm it exists.
            return Result<ResolvedPath>.NotFound($"Source {sourceId} was not found.");
        }

        string scope = ResolveScope(source, principal);
        scope = UserHomePath.AlignScopeWithExistingDirectory(
            source.Path,
            scope,
            userName: principal.UserName,
            displayName: principal.DisplayName);

        if (!TryEnsureUserHomeDirectory(source, scope, out string? homeError))
        {
            return Result<ResolvedPath>.Invalid(new ValidationError(nameof(virtualPath), homeError!));
        }

        string scoped = VirtualPath.Normalize(virtualPath);
        string absolute = scope == Constants.RootPath
            ? scoped
            : VirtualPath.Combine(scope, scoped.TrimStart('/'));

        if (!VirtualPath.TryResolve(source.Path, absolute, out string? physicalPath))
        {
            logger.LogWarning(
                "Rejected path '{Path}' on source {SourceId}: resolves outside the source root",
                virtualPath,
                sourceId);

            return Result<ResolvedPath>.Invalid(new ValidationError(nameof(virtualPath), "Invalid path."));
        }

        var rules = await GetRulesAsync(sourceId, cancellationToken);
        var resolution = PermissionResolver.Resolve(source, rules, principal, absolute);

        if (!resolution.Has(required))
        {
            // Missing Browse means the path should look as though it isn't there; anything else
            // is an honest permission failure the user can act on.
            if (!resolution.Has(FilePermission.Browse))
            {
                return Result<ResolvedPath>.NotFound("The requested path was not found.");
            }

            var missing = required & ~resolution.Effective;
            return Result<ResolvedPath>.Forbidden(
                $"You do not have permission to {Describe(missing)} here.");
        }

        return Result<ResolvedPath>.Success(new ResolvedPath
        {
            Source = source,
            PhysicalPath = physicalPath,
            AbsoluteVirtualPath = absolute,
            ScopedVirtualPath = scoped,
            Scope = scope,
            Permissions = resolution.Effective,
        });
    }

    public async Task<Func<string, FilePermission>> GetChildPermissionEvaluatorAsync(
        int sourceId,
        string directoryVirtualPath,
        AccessPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var source = await GetSourceAsync(sourceId, cancellationToken);
        if (source is null)
        {
            return _ => FilePermission.None;
        }

        var allRules = await GetRulesAsync(sourceId, cancellationToken);
        var relevant = PermissionResolver.FilterForSubtree(allRules, principal, directoryVirtualPath);

        // An administrator on a bypassing source, or a source with no rules touching this
        // subtree, gives every child the same answer — compute it once.
        if (relevant.Count == 0 || (principal.IsAdministrator && source.AdministratorsBypassRules))
        {
            var uniform = PermissionResolver.Resolve(source, relevant, principal, directoryVirtualPath).Effective;
            return _ => uniform;
        }

        var memo = new Dictionary<string, FilePermission>(StringComparer.OrdinalIgnoreCase);

        return absolutePath =>
        {
            if (memo.TryGetValue(absolutePath, out var cached))
            {
                return cached;
            }

            var effective = PermissionResolver.Resolve(source, relevant, principal, absolutePath).Effective;
            memo[absolutePath] = effective;
            return effective;
        };
    }

    public async Task<Result<EffectivePermissionsDto>> ExplainAsync(
        EffectivePermissionsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result<EffectivePermissionsDto>.Forbidden();
        }

        var source = await GetSourceAsync(request.SourceId, cancellationToken);
        if (source is null)
        {
            return Result<EffectivePermissionsDto>.NotFound($"Source {request.SourceId} was not found.");
        }

        var principal = await BuildPrincipalForAsync(request.UserId, cancellationToken);
        var rules = await GetRulesAsync(request.SourceId, cancellationToken);
        string scope = ResolveScope(source, principal);
        string absolute = scope == Constants.RootPath
            ? VirtualPath.Normalize(request.Path)
            : VirtualPath.Combine(scope, VirtualPath.Normalize(request.Path).TrimStart('/'));

        var resolution = PermissionResolver.Resolve(source, rules, principal, absolute, includeTrace: true);

        var subjectNames = await ResolveSubjectNamesAsync(resolution.Trace, cancellationToken);

        return Result<EffectivePermissionsDto>.Success(new EffectivePermissionsDto(
            request.SourceId,
            absolute,
            principal.UserId,
            principal.UserName,
            resolution.Effective,
            resolution.IsAdministratorBypass,
            resolution.IsClampedByReadOnlySource,
            resolution.Trace
                .Select(t => new PermissionTraceEntryDto(
                    t.Description,
                    t.RuleId,
                    t.RulePath,
                    t.SubjectType,
                    t.SubjectId is null ? null : subjectNames.GetValueOrDefault(t.SubjectId, t.SubjectId),
                    t.Effect,
                    t.RulePermissions,
                    t.Before,
                    t.After))
                .ToList()));
    }

    public async Task<IReadOnlyList<SourceSummaryDto>> GetAccessibleSourcesAsync(
        CancellationToken cancellationToken = default)
    {
        if (accessibleSources is not null)
        {
            return accessibleSources;
        }

        await accessibleSourcesLock.WaitAsync(cancellationToken);

        try
        {
            if (accessibleSources is not null)
            {
                return accessibleSources;
            }

            var principal = await GetCurrentPrincipalAsync(cancellationToken);
            var sources = await GetAllSourcesAsync(cancellationToken);
            var result = new List<SourceSummaryDto>();

            foreach (var source in sources.Where(s => !s.IsDisabled && s.IsVisibleInSidebar))
            {
                var rules = await GetRulesAsync(source.Id, cancellationToken);
                string scope = ResolveScope(source, principal);
                var effective = PermissionResolver.Resolve(source, rules, principal, scope).Effective;

                // A deny-by-default source with per-user home directories grants nothing at the root
                // yet must still appear, or the user has no way to reach their own folder.
                bool visible = effective.HasAny(FilePermission.Browse)
                    || PermissionResolver.HasAnyBrowsableDescendant(source, rules, principal, scope);

                if (!visible)
                {
                    continue;
                }

                result.Add(new SourceSummaryDto(
                    source.Id,
                    source.Name,
                    source.Description,
                    effective,
                    source.IsReadOnly,
                    source.IndexedItemCount,
                    source.IndexedSizeBytes,
                    source.LastIndexedAt));
            }

            return accessibleSources = result;
        }
        finally
        {
            accessibleSourcesLock.Release();
        }
    }

    public void InvalidateSource(int sourceId)
    {
        cache.Remove(RulesCacheKey(sourceId));
        cache.Remove(SourcesCacheKey);
        accessibleSources = null;
    }

    /// <summary>
    /// The caller's starting directory in a source, with <c>${username}</c> expanded. Everything
    /// they browse is relative to this, so a scoped user can't even name a path outside it.
    /// Token expansion only — no disk I/O — so the sidebar cannot hang enumerating a NAS folder.
    /// </summary>
    private static string ResolveScope(Source source, AccessPrincipal principal)
    {
        if (string.IsNullOrWhiteSpace(source.DefaultUserScope))
        {
            return Constants.RootPath;
        }

        string expanded = VirtualPath.ExpandTokens(
            source.DefaultUserScope,
            principal.FolderName,
            principal.UserId);

        // An anonymous visitor has no name to substitute; falling back to the root would hand
        // them the whole source, so deny by giving them an unreachable scope instead.
        if (VirtualPath.HasTokens(expanded))
        {
            return Constants.RootPath;
        }

        return VirtualPath.Normalize(expanded);
    }

    /// <summary>
    /// Creates the caller's home folder when the source is configured to do so. Without this,
    /// the first visit to a per-user source is a 404 until someone makes the directory by hand.
    /// </summary>
    private bool TryEnsureUserHomeDirectory(Source source, string scope, out string? error)
    {
        error = null;

        if (!source.CreateUserDirectory || scope == Constants.RootPath)
        {
            return true;
        }

        if (!VirtualPath.TryResolve(source.Path, scope, out string? homePhysical))
        {
            return true;
        }

        if (Directory.Exists(homePhysical))
        {
            return true;
        }

        try
        {
            Directory.CreateDirectory(homePhysical);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                ex,
                "Could not create home directory {Physical} for source {SourceId}",
                homePhysical,
                source.Id);

            error =
                $"Could not create the user folder '{homePhysical}'. " +
                "The process inside the container must be able to write there — " +
                "a share ACL on the NAS is not enough if this folder is bind-mounted.";
            return false;
        }
    }

    /// <summary>
    /// Builds a principal for an arbitrary user, which the simulator needs so an administrator
    /// can ask "what would Bob see here?". Roles come from <see cref="IUserInfoService"/> rather
    /// than the Identity tables so this also works under Keycloak.
    /// </summary>
    private async Task<AccessPrincipal> BuildPrincipalForAsync(string? userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(userId) || userId == Constants.AnonymousUserId)
        {
            return AccessPrincipal.Anonymous;
        }

        var info = await userInfoService.GetUserAsync(userId, cancellationToken);

        var groupIds = (await groupMemberRepository.FindAsync(new SearchOptions<AccessGroupMember>
        {
            Query = m => m.UserId == userId,
            CancellationToken = cancellationToken,
        })).Select(m => m.AccessGroupId).ToHashSet();

        var localUser = await FindLocalUserAsync(userId, cancellationToken);

        var roles = info?.Roles.ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return new AccessPrincipal
        {
            UserId = userId,
            UserName = info?.UserName ?? localUser?.UserName,
            DisplayName = info?.DisplayName ?? localUser?.DisplayName,
            IsAuthenticated = true,
            IsAdministrator = roles.Contains(Constants.Roles.Administrator),
            RoleNames = roles,
            GroupIds = groupIds,
            Ceiling = localUser is not null && !localUser.CanCreateShares
                ? FilePermission.Full & ~FilePermission.Share
                : FilePermission.Full,
        };
    }

    private async Task<Dictionary<string, string>> ResolveSubjectNamesAsync(
        IReadOnlyList<PermissionTraceEntry> trace,
        CancellationToken cancellationToken)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);

        var userIds = trace
            .Where(t => t.SubjectType == AccessSubjectType.User && t.SubjectId is not null)
            .Select(t => t.SubjectId!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (userIds.Count > 0)
        {
            var users = await FindLocalUsersAsync(userIds, cancellationToken);

            foreach (var user in users)
            {
                names[user.Id] = user.DisplayName ?? user.UserName ?? user.Id;
            }
        }

        return names;
    }

    private const string SourcesCacheKey = "arcafs.sources.all";

    private static string RulesCacheKey(int sourceId) => $"arcafs.rules.{sourceId}";

    private async Task<Source?> GetSourceAsync(int sourceId, CancellationToken cancellationToken)
    {
        var sources = await GetAllSourcesAsync(cancellationToken);
        return sources.FirstOrDefault(s => s.Id == sourceId);
    }

    private async Task<IReadOnlyList<Source>> GetAllSourcesAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(SourcesCacheKey, out IReadOnlyList<Source>? cached) && cached is not null)
        {
            return cached;
        }

        var sources = (await sourceRepository.FindAsync(new SearchOptions<Source>
        {
            OrderBy = q => q.OrderBy(s => s.SortOrder).ThenBy(s => s.Name),
            CancellationToken = cancellationToken,
        })).ToList();

        cache.Set(SourcesCacheKey, (IReadOnlyList<Source>)sources, CacheDuration);
        return sources;
    }

    private async Task<IReadOnlyList<AccessRule>> GetRulesAsync(int sourceId, CancellationToken cancellationToken)
    {
        string key = RulesCacheKey(sourceId);
        if (cache.TryGetValue(key, out IReadOnlyList<AccessRule>? cached) && cached is not null)
        {
            return cached;
        }

        var rules = (await accessRuleRepository.FindAsync(new SearchOptions<AccessRule>
        {
            Query = r => r.SourceId == sourceId,
            CancellationToken = cancellationToken,
        })).ToList();

        cache.Set(key, (IReadOnlyList<AccessRule>)rules, CacheDuration);
        return rules;
    }

    /// <summary>
    /// Identity rows are read through Extenso's factory so each call gets its own context.
    /// The scoped <c>ApplicationDbContextBase</c> is shared across the Blazor circuit and
    /// cannot run two queries at once — which is exactly what the home page and sidebar do.
    /// </summary>
    private async Task<ApplicationUser?> FindLocalUserAsync(string userId, CancellationToken cancellationToken)
    {
        await using var context = (ApplicationDbContextBase)dbContextFactory.GetContext();
        return await context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
    }

    private async Task<List<ApplicationUser>> FindLocalUsersAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken)
    {
        await using var context = (ApplicationDbContextBase)dbContextFactory.GetContext();
        return await context.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToListAsync(cancellationToken);
    }

    private static string Describe(FilePermission missing) =>
        missing == FilePermission.None
            ? "do that"
            : string.Join(", ", missing.ToFlags().Select(f => f.ToDisplayName().ToLowerInvariant()));
}
