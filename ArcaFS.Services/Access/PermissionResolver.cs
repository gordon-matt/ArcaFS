namespace ArcaFS.Services.Access;

/// <summary>
/// Turns a source's access rules into a concrete <see cref="FilePermission"/> mask for one
/// principal at one path. Pure and synchronous — all I/O happens in <see cref="PermissionService"/>,
/// which loads and caches the rules and then calls in here.
/// </summary>
/// <remarks>
/// <para><b>Algorithm.</b> Start from the source baseline, then apply every matching rule in
/// order of increasing authority, each one setting or clearing the bits it names:</para>
/// <list type="number">
/// <item>Shallower rule path first, so <c>/shared/private</c> overrides <c>/shared</c>.</item>
/// <item>Then less specific subject first, per <see cref="AccessSubjectType"/>'s numeric order,
/// so a <c>User</c> rule overrides the <c>Group</c> rule that would otherwise cover them.</item>
/// <item>Then lower <c>Priority</c> first, as an explicit tie-break.</item>
/// <item>Then <c>Allow</c> before <c>Deny</c>, so deny wins a genuine tie.</item>
/// </list>
/// <para>Because each rule only touches the bits in its own mask, a deny of <c>Delete</c> at a
/// subfolder leaves an inherited <c>Create</c> intact. That is the property that makes
/// "read-only for everyone except me, on this one source" expressible, which is what File
/// Browser Quantum's global per-user flags and source-wide <c>readOnly</c> switch cannot do.</para>
/// </remarks>
public static class PermissionResolver
{
    /// <summary>
    /// Resolves the effective permissions for <paramref name="principal"/> at
    /// <paramref name="normalizedPath"/> within <paramref name="source"/>.
    /// </summary>
    /// <param name="rules">
    /// Rules belonging to <paramref name="source"/>. Passing a superset is harmless — anything
    /// that doesn't match the path or subject is ignored — so callers may pass a cached list.
    /// </param>
    /// <param name="normalizedPath">Must already be <see cref="VirtualPath.Normalize"/>d.</param>
    /// <param name="includeTrace">
    /// Build the human-readable explanation. Off by default because listings resolve once per
    /// child and the trace would dominate the cost.
    /// </param>
    public static PermissionResolution Resolve(
        Source source,
        IReadOnlyList<AccessRule> rules,
        AccessPrincipal principal,
        string normalizedPath,
        bool includeTrace = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(principal);

        var trace = includeTrace ? new List<PermissionTraceEntry>() : null;

        if (source.IsDisabled)
        {
            Add(trace, "Source is disabled.", FilePermission.None, FilePermission.None);
            return new PermissionResolution { Effective = FilePermission.None, Trace = Materialize(trace) };
        }

        if (principal.IsAdministrator && source.AdministratorsBypassRules)
        {
            var adminGrant = Clamp(FilePermission.Full, source, principal, trace, out bool adminClamped);
            Add(trace, "Administrator bypass: rule evaluation skipped.", FilePermission.None, adminGrant);

            return new PermissionResolution
            {
                Effective = adminGrant,
                IsAdministratorBypass = true,
                IsClampedByReadOnlySource = adminClamped,
                Trace = Materialize(trace),
            };
        }

        var granted = source.DenyByDefault ? FilePermission.None : source.DefaultPermissions;
        Add(
            trace,
            source.DenyByDefault
                ? "Source baseline: deny by default."
                : $"Source baseline: default permissions ({granted.ToDisplayName()}).",
            FilePermission.None,
            granted);

        foreach (var candidate in MatchingRules(rules, principal, normalizedPath))
        {
            var before = granted;
            var rule = candidate.Rule;

            granted = rule.Effect == AccessEffect.Allow
                ? granted | rule.Permissions
                : granted & ~rule.Permissions;

            if (trace is not null)
            {
                trace.Add(new PermissionTraceEntry
                {
                    Description = $"{rule.Effect} {Describe(rule.Permissions)} for {Describe(rule)} at {candidate.ExpandedPath}",
                    RuleId = rule.Id,
                    RulePath = candidate.ExpandedPath,
                    SubjectType = rule.SubjectType,
                    SubjectId = rule.SubjectId,
                    Effect = rule.Effect,
                    RulePermissions = rule.Permissions,
                    Before = before,
                    After = granted,
                });
            }
        }

        granted = Clamp(granted, source, principal, trace, out bool clamped);

        return new PermissionResolution
        {
            Effective = granted,
            IsClampedByReadOnlySource = clamped,
            Trace = Materialize(trace),
        };
    }

    /// <summary>
    /// Narrows a source's full rule set to those that could possibly affect
    /// <paramref name="directoryPath"/> or anything directly inside it. Listing a folder resolves
    /// permissions once per child; pre-filtering here keeps that from being O(children × rules).
    /// </summary>
    public static IReadOnlyList<AccessRule> FilterForSubtree(
        IReadOnlyList<AccessRule> rules,
        AccessPrincipal principal,
        string directoryPath)
    {
        var relevant = new List<AccessRule>(rules.Count);

        foreach (var rule in rules)
        {
            string expanded = ExpandRulePath(rule, principal);

            // Rules at or above the directory are inherited by it and its children; rules below
            // it may apply to a specific child. Anything on a different branch cannot matter.
            if (VirtualPath.IsSameOrUnder(directoryPath, expanded)
                || VirtualPath.IsSameOrUnder(expanded, directoryPath))
            {
                relevant.Add(rule);
            }
        }

        return relevant;
    }

    /// <summary>
    /// True when <paramref name="principal"/> can see anything at all under
    /// <paramref name="path"/>. Lets a parent folder stay visible when the only thing the user
    /// is allowed to reach is buried a few levels down, which is how deny-by-default sources
    /// with per-user home directories remain navigable.
    /// </summary>
    public static bool HasAnyBrowsableDescendant(
        Source source,
        IReadOnlyList<AccessRule> rules,
        AccessPrincipal principal,
        string path)
    {
        if (Resolve(source, rules, principal, path).Has(FilePermission.Browse))
        {
            return true;
        }

        foreach (var rule in rules)
        {
            if (rule.Effect != AccessEffect.Allow || !rule.Permissions.HasAny(FilePermission.Browse))
            {
                continue;
            }

            string expanded = ExpandRulePath(rule, principal);
            if (!VirtualPath.IsStrictlyUnder(expanded, path))
            {
                continue;
            }

            if (SubjectMatches(rule, principal, out _)
                && Resolve(source, rules, principal, expanded).Has(FilePermission.Browse))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<RuleCandidate> MatchingRules(
        IReadOnlyList<AccessRule> rules,
        AccessPrincipal principal,
        string normalizedPath)
    {
        var candidates = new List<RuleCandidate>();

        foreach (var rule in rules)
        {
            if (!SubjectMatches(rule, principal, out int specificity))
            {
                continue;
            }

            string expanded = ExpandRulePath(rule, principal);

            // An unexpanded token means the rule was written for a real user but is being
            // evaluated for someone without a name (an anonymous share visitor). Matching it
            // literally would grant access to a directory called "${username}", so skip it.
            if (VirtualPath.HasTokens(expanded))
            {
                continue;
            }

            if (!AppliesToPath(expanded, rule.Propagation, normalizedPath))
            {
                continue;
            }

            candidates.Add(new RuleCandidate(rule, expanded, specificity, VirtualPath.Depth(expanded)));
        }

        return candidates
            .OrderBy(c => c.PathDepth)
            .ThenBy(c => c.SubjectSpecificity)
            .ThenBy(c => c.Rule.Priority)
            .ThenBy(c => (int)c.Rule.Effect)
            .ThenBy(c => c.Rule.Id);
    }

    private static bool AppliesToPath(string rulePath, AccessPropagation propagation, string targetPath) =>
        propagation switch
        {
            AccessPropagation.ThisPathAndChildren => VirtualPath.IsSameOrUnder(targetPath, rulePath),
            AccessPropagation.ThisPathOnly => targetPath.Equals(rulePath, StringComparison.OrdinalIgnoreCase),
            AccessPropagation.ChildrenOnly => VirtualPath.IsStrictlyUnder(targetPath, rulePath),
            _ => false,
        };

    /// <summary>
    /// Whether the rule's subject covers the principal. <paramref name="specificity"/> receives
    /// the <see cref="AccessSubjectType"/> ordinal, which is the resolver's ordering key.
    /// </summary>
    private static bool SubjectMatches(AccessRule rule, AccessPrincipal principal, out int specificity)
    {
        specificity = (int)rule.SubjectType;

        return rule.SubjectType switch
        {
            AccessSubjectType.Everyone => true,

            AccessSubjectType.Anonymous => !principal.IsAuthenticated,

            AccessSubjectType.Authenticated => principal.IsAuthenticated,

            AccessSubjectType.Role => principal.IsAuthenticated
                && rule.SubjectId is not null
                && principal.RoleNames.Contains(rule.SubjectId),

            AccessSubjectType.Group => principal.IsAuthenticated
                && int.TryParse(rule.SubjectId, out int groupId)
                && principal.GroupIds.Contains(groupId),

            // Owner rules match any signed-in user; the ${username} token in the rule's path is
            // what confines the grant to that user's own directory.
            AccessSubjectType.Owner => principal.IsAuthenticated,

            AccessSubjectType.User => rule.SubjectId is not null
                && string.Equals(rule.SubjectId, principal.UserId, StringComparison.Ordinal),

            _ => false,
        };
    }

    private static string ExpandRulePath(AccessRule rule, AccessPrincipal principal) =>
        VirtualPath.Normalize(VirtualPath.ExpandTokens(rule.Path, principal.UserName, principal.UserId));

    private static FilePermission Clamp(
        FilePermission granted,
        Source source,
        AccessPrincipal principal,
        List<PermissionTraceEntry>? trace,
        out bool clampedByReadOnlySource)
    {
        clampedByReadOnlySource = false;

        if (source.IsReadOnly)
        {
            var before = granted;
            granted &= FilePermissionExtensions.NonMutating;
            clampedByReadOnlySource = before != granted;

            if (clampedByReadOnlySource)
            {
                Add(trace, "Source is read-only: mutating permissions removed.", before, granted);
            }
        }

        if (principal.Ceiling != FilePermission.Full)
        {
            var before = granted;
            granted &= principal.Ceiling;

            if (before != granted)
            {
                Add(trace, $"Principal ceiling applied ({principal.Ceiling.ToDisplayName()}).", before, granted);
            }
        }

        return granted;
    }

    private static void Add(
        List<PermissionTraceEntry>? trace,
        string description,
        FilePermission before,
        FilePermission after) =>
        trace?.Add(new PermissionTraceEntry
        {
            Description = description,
            Before = before,
            After = after,
        });

    private static IReadOnlyList<PermissionTraceEntry> Materialize(List<PermissionTraceEntry>? trace) =>
        trace is null ? [] : trace;

    private static string Describe(FilePermission permissions) =>
        permissions == FilePermission.None
            ? "nothing"
            : string.Join(", ", permissions.ToFlags().Select(f => f.ToDisplayName()));

    private static string Describe(AccessRule rule) => rule.SubjectType switch
    {
        AccessSubjectType.Everyone => "everyone",
        AccessSubjectType.Anonymous => "anonymous visitors",
        AccessSubjectType.Authenticated => "signed-in users",
        AccessSubjectType.Owner => "the path owner",
        AccessSubjectType.Role => $"role '{rule.SubjectId}'",
        AccessSubjectType.Group => $"group #{rule.SubjectId}",
        AccessSubjectType.User => $"user '{rule.SubjectId}'",
        _ => rule.SubjectType.ToString(),
    };

    private readonly record struct RuleCandidate(
        AccessRule Rule,
        string ExpandedPath,
        int SubjectSpecificity,
        int PathDepth);
}
