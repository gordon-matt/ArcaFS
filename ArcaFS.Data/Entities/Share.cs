namespace ArcaFS.Data.Entities;

/// <summary>
/// A public link to one path. A share is its own authorization context: visitors are
/// <see cref="Constants.AnonymousUserId"/> and get exactly the mask implied by
/// <see cref="Mode"/>, clamped to what the creator themselves could do at that path.
/// </summary>
public class Share : BaseEntity<int>
{
    /// <summary>URL-safe random token forming the public link.</summary>
    public required string Token { get; set; }

    public int SourceId { get; set; }

    /// <summary>Normalized virtual path being shared. May be a file or a folder.</summary>
    public required string Path { get; set; }

    public required string OwnerUserId { get; set; }

    public string? Title { get; set; }

    public string? Description { get; set; }

    public ShareMode Mode { get; set; } = ShareMode.ReadOnly;

    /// <summary>BCrypt-style hash of the access password. Null means no password.</summary>
    public string? PasswordHash { get; set; }

    public DateTime? ExpiresAt { get; set; }

    /// <summary>Refuses further access once <see cref="DownloadCount"/> reaches it. Null is unlimited.</summary>
    public int? MaxDownloads { get; set; }

    public int DownloadCount { get; set; }

    /// <summary>Suppresses thumbnail generation for share visitors.</summary>
    public bool DisableThumbnails { get; set; }

    /// <summary>Hides the in-browser viewer so recipients can only download.</summary>
    public bool DisableFileViewer { get; set; }

    /// <summary>Bootswatch theme forced on the public share page.</summary>
    public string? Theme { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastAccessedAt { get; set; }

    public bool IsDisabled { get; set; }

    public virtual Source Source { get; set; } = null!;
}

public class ShareMap : IEntityTypeConfiguration<Share>
{
    public void Configure(EntityTypeBuilder<Share> builder)
    {
        builder.ToTable("Shares", Constants.Schemas.App);
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Token).IsRequired().HasMaxLength(Constants.MaxLengths.ShareToken);
        builder.Property(m => m.Path).IsRequired().HasMaxLength(Constants.MaxLengths.Path).IsUnicode(true);
        builder.Property(m => m.OwnerUserId).IsRequired().HasMaxLength(450);
        builder.Property(m => m.Title).HasMaxLength(256).IsUnicode(true);
        builder.Property(m => m.Description).HasMaxLength(1024).IsUnicode(true);
        builder.Property(m => m.PasswordHash).HasMaxLength(256);
        builder.Property(m => m.Theme).HasMaxLength(64);
        builder.Property(m => m.Mode).HasConversion<int>();

        builder.HasIndex(m => m.Token).IsUnique();
        builder.HasIndex(m => m.OwnerUserId);

        builder.HasOne(m => m.Source)
            .WithMany()
            .HasForeignKey(m => m.SourceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}