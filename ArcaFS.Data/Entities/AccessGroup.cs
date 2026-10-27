namespace ArcaFS.Data.Entities;

/// <summary>
/// A named bag of users that access rules can target. Deliberately separate from Identity roles:
/// roles carry application privilege (is this person an administrator), groups carry filesystem
/// intent (who is in "Family"). Keycloak deployments can map IdP groups onto these by name.
/// </summary>
public class AccessGroup : BaseEntity<int>
{
    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>
    /// Group is kept in step with an external identity provider claim of the same name. Members
    /// are replaced on each sign-in, so local edits would be overwritten and are blocked.
    /// </summary>
    public bool IsExternallyManaged { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<AccessGroupMember> Members { get; set; } = [];
}

public class AccessGroupMap : IEntityTypeConfiguration<AccessGroup>
{
    public void Configure(EntityTypeBuilder<AccessGroup> builder)
    {
        builder.ToTable("AccessGroups", Constants.Schemas.App);
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Name).IsRequired().HasMaxLength(Constants.MaxLengths.GroupName).IsUnicode(true);
        builder.Property(m => m.Description).HasMaxLength(512).IsUnicode(true);

        builder.HasIndex(m => m.Name).IsUnique();
    }
}

public class AccessGroupMember : BaseEntity<int>
{
    public int AccessGroupId { get; set; }

    public required string UserId { get; set; }

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    public virtual AccessGroup AccessGroup { get; set; } = null!;

    public virtual ApplicationUser? User { get; set; }
}

public class AccessGroupMemberMap : IEntityTypeConfiguration<AccessGroupMember>
{
    public void Configure(EntityTypeBuilder<AccessGroupMember> builder)
    {
        builder.ToTable("AccessGroupMembers", Constants.Schemas.App);
        builder.HasKey(m => m.Id);

        builder.Property(m => m.UserId).IsRequired().HasMaxLength(450);

        builder.HasIndex(m => new { m.AccessGroupId, m.UserId }).IsUnique();
        builder.HasIndex(m => m.UserId);

        builder.HasOne(m => m.AccessGroup)
            .WithMany(m => m.Members)
            .HasForeignKey(m => m.AccessGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        // Keycloak-sourced members have no local ApplicationUser row, so this FK stays optional.
        builder.HasOne(m => m.User)
            .WithMany(m => m.GroupMemberships)
            .HasForeignKey(m => m.UserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);
    }
}