namespace ArcaFS.Data.Entities;

/// <summary>
/// A root directory on disk exposed to the browser under a friendly name. Everything the user
/// sees is addressed as (source, virtual path), and access rules hang off a source.
/// </summary>
public class Source : BaseEntity<int>
{
    /// <summary>Display name, and the first URL segment under <c>/files</c>.</summary>
    public required string Name { get; set; }

    /// <summary>Absolute path on the host filesystem.</summary>
    public required string Path { get; set; }

    public string? Description { get; set; }

    /// <summary>Hidden from every user, including administrators. Keeps config without serving it.</summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    /// Clamps every effective permission to the non-mutating set, whoever is asking. A blunt
    /// instrument for genuinely immutable archives; per-user read-only is done with rules instead.
    /// </summary>
    public bool IsReadOnly { get; set; }

    /// <summary>
    /// When true the source starts from <see cref="FilePermission.None"/> and only explicit
    /// allow rules grant anything. When false, <see cref="DefaultPermissions"/> is the baseline.
    /// </summary>
    public bool DenyByDefault { get; set; }

    /// <summary>
    /// Baseline grant for any principal with no matching rule. Ignored when
    /// <see cref="DenyByDefault"/> is set. Defaults to read-only, which makes the common
    /// "everyone can look, nobody can touch" source a zero-rule configuration.
    /// </summary>
    public FilePermission DefaultPermissions { get; set; } = FilePermission.ReadOnly;

    /// <summary>
    /// Whether members of <see cref="Heroicsoft.NetForge.Abstractions.Auth.WellKnownRoles.Administrator"/> skip rule evaluation.
    /// On by default so deny-by-default sources do not lock administrators out of their own
    /// server. Opting into that lock is explicit.
    /// </summary>
    public bool AdministratorsBypassRules { get; set; } = true;

    /// <summary>
    /// Per-user starting directory, relative to the source root, supporting
    /// <c>${username}</c> / <c>${userid}</c>. When set, a user's browser only ever sees paths
    /// inside their own scope — the prefix is stripped from every path they receive.
    /// </summary>
    public string? DefaultUserScope { get; set; }

    /// <summary>Creates <see cref="DefaultUserScope"/> on first access if it doesn't exist.</summary>
    public bool CreateUserDirectory { get; set; }

    /// <summary>Show this source in the sidebar for users who have any access to it.</summary>
    public bool IsVisibleInSidebar { get; set; } = true;

    /// <summary>Exclude dot-files from listings unless the user opts in.</summary>
    public bool IgnoreHiddenFiles { get; set; } = true;

    /// <summary>Never follow symlinks when walking or serving. Off by default for safety.</summary>
    public bool FollowSymlinks { get; set; }

    /// <summary>Include this source in the background indexer, enabling search over it.</summary>
    public bool IsIndexed { get; set; } = true;

    /// <summary>Minutes between automatic index scans. Zero disables the schedule.</summary>
    public int IndexIntervalMinutes { get; set; } = 60;

    /// <summary>Newline-separated glob patterns excluded from indexing and listings.</summary>
    public string? ExcludePatterns { get; set; }

    public DateTime? LastIndexedAt { get; set; }

    public long? IndexedItemCount { get; set; }

    public long? IndexedSizeBytes { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<AccessRule> AccessRules { get; set; } = [];

    public virtual ICollection<IndexedItem> IndexedItems { get; set; } = [];
}

public class SourceMap : IEntityTypeConfiguration<Source>
{
    public void Configure(EntityTypeBuilder<Source> builder)
    {
        builder.ToTable("Sources", Constants.Schemas.App);
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Name).IsRequired().HasMaxLength(Constants.MaxLengths.SourceName).IsUnicode(true);
        builder.Property(m => m.Path).IsRequired().HasMaxLength(Constants.MaxLengths.Path).IsUnicode(true);
        builder.Property(m => m.Description).HasMaxLength(1024).IsUnicode(true);
        builder.Property(m => m.DefaultUserScope).HasMaxLength(Constants.MaxLengths.Path).IsUnicode(true);
        builder.Property(m => m.ExcludePatterns).HasMaxLength(4000).IsUnicode(true);
        builder.Property(m => m.DefaultPermissions).HasConversion<int>();

        builder.HasIndex(m => m.Name).IsUnique();
    }
}
