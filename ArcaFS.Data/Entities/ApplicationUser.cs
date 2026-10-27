using Microsoft.AspNetCore.Identity;

namespace ArcaFS.Data.Entities;

public class ApplicationUser : IdentityUser
{
    public string? DisplayName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastSeenAt { get; set; }

    /// <summary>
    /// Global cap on link sharing. Access rules can grant <see cref="FilePermission.Share"/> per
    /// path, but this switch overrides them — turning it off stops a user sharing anything at all
    /// without having to hunt down every rule.
    /// </summary>
    public bool CanCreateShares { get; set; } = true;

    /// <summary>Allows minting API tokens for WebDAV and scripted access.</summary>
    public bool CanUseApi { get; set; }

    /// <summary>Bootswatch theme override. Null means "use the server default".</summary>
    public string? PreferredTheme { get; set; }

    public ListingViewMode PreferredViewMode { get; set; } = ListingViewMode.Normal;

    public bool ShowHiddenFiles { get; set; }

    public bool SingleClickToOpen { get; set; }

    public virtual ICollection<AccessGroupMember> GroupMemberships { get; set; } = [];
}

public class ApplicationUserMap : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(m => m.DisplayName).HasMaxLength(256).IsUnicode(true);
        builder.Property(m => m.PreferredTheme).HasMaxLength(64);
    }
}