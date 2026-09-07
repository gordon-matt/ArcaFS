namespace ArcaFS.Services.Access;

/// <summary>The resolver's verdict for one (source, path, principal) triple.</summary>
public sealed record PermissionResolution
{
    public FilePermission Effective { get; init; }

    /// <summary>Rule evaluation was skipped because the caller is an administrator.</summary>
    public bool IsAdministratorBypass { get; init; }

    /// <summary>Mutating permissions were stripped because the source is marked read-only.</summary>
    public bool IsClampedByReadOnlySource { get; init; }

    /// <summary>
    /// Ordered account of how <see cref="Effective"/> was arrived at. Empty unless the caller
    /// asked for it — building it costs allocations on a path that runs once per listed file.
    /// </summary>
    public IReadOnlyList<PermissionTraceEntry> Trace { get; init; } = [];

    public bool Has(FilePermission required) => Effective.Has(required);
}

/// <summary>One step of the resolution, bracketed by the mask before and after it applied.</summary>
public sealed record PermissionTraceEntry
{
    public required string Description { get; init; }

    public int? RuleId { get; init; }

    public string? RulePath { get; init; }

    public AccessSubjectType? SubjectType { get; init; }

    public string? SubjectId { get; init; }

    public AccessEffect? Effect { get; init; }

    public FilePermission RulePermissions { get; init; }

    public FilePermission Before { get; init; }

    public FilePermission After { get; init; }
}
