namespace ArcaFS.Data.Entities;

/// <summary>
/// One entry in a source's access control list: "at this path, this subject is allowed/denied
/// these operations, propagating this far down the tree".
/// <para>
/// Rules are additive and layered rather than exclusive. The resolver starts from the source
/// baseline and applies every matching rule in order of increasing specificity — shallower path
/// first, then less specific subject, then <c>Allow</c> before <c>Deny</c> — so the most specific
/// statement about a permission bit is the one that sticks. See
/// <c>ArcaFS.Services.Access.PermissionResolver</c> for the implementation.
/// </para>
/// </summary>
public class AccessRule : BaseEntity<int>
{
    public int SourceId { get; set; }

    /// <summary>
    /// Normalized virtual path the rule is anchored at; <c>/</c> means the whole source. May
    /// contain <c>${username}</c> / <c>${userid}</c>, which are expanded per principal at
    /// evaluation time — that is how a single rule gives every user their own home directory.
    /// </summary>
    public string Path { get; set; } = Constants.RootPath;

    public AccessSubjectType SubjectType { get; set; }

    /// <summary>
    /// User id, group id or role name depending on <see cref="SubjectType"/>. Null for the
    /// subject types that don't need one (Everyone / Anonymous / Authenticated / Owner).
    /// </summary>
    public string? SubjectId { get; set; }

    public AccessEffect Effect { get; set; }

    public FilePermission Permissions { get; set; }

    public AccessPropagation Propagation { get; set; } = AccessPropagation.ThisPathAndChildren;

    /// <summary>
    /// Tie-break for two rules that are otherwise equally specific. Higher wins because it is
    /// applied later. Rarely needed; exists so an admin can force an ordering without inventing
    /// a deeper path.
    /// </summary>
    public int Priority { get; set; }

    /// <summary>Free-text note shown in the rules editor. Never interpreted.</summary>
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string? CreatedByUserId { get; set; }

    public virtual Source Source { get; set; } = null!;
}

public class AccessRuleMap : IEntityTypeConfiguration<AccessRule>
{
    public void Configure(EntityTypeBuilder<AccessRule> builder)
    {
        builder.ToTable("AccessRules", Constants.Schemas.App);
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Path).IsRequired().HasMaxLength(Constants.MaxLengths.Path).IsUnicode(true);
        builder.Property(m => m.SubjectId).HasMaxLength(450);
        builder.Property(m => m.Description).HasMaxLength(512).IsUnicode(true);
        builder.Property(m => m.CreatedByUserId).HasMaxLength(450);
        builder.Property(m => m.SubjectType).HasConversion<int>();
        builder.Property(m => m.Effect).HasConversion<int>();
        builder.Property(m => m.Permissions).HasConversion<int>();
        builder.Property(m => m.Propagation).HasConversion<int>();

        // The resolver loads every rule for a source and filters in memory, so this index is
        // what keeps that load cheap as the ACL grows.
        builder.HasIndex(m => new { m.SourceId, m.Path });
        builder.HasIndex(m => new { m.SubjectType, m.SubjectId });

        builder.HasOne(m => m.Source)
            .WithMany(m => m.AccessRules)
            .HasForeignKey(m => m.SourceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}