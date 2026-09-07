namespace ArcaFS.Data.Entities;

/// <summary>A path a user pinned to their sidebar for quick access.</summary>
public class FavoriteItem : BaseEntity<int>
{
    public required string UserId { get; set; }

    public int SourceId { get; set; }

    public required string Path { get; set; }

    /// <summary>Overrides the folder name in the sidebar. Null uses the last path segment.</summary>
    public string? Label { get; set; }

    public bool IsDirectory { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Source Source { get; set; } = null!;
}

public class FavoriteItemMap : IEntityTypeConfiguration<FavoriteItem>
{
    public void Configure(EntityTypeBuilder<FavoriteItem> builder)
    {
        builder.ToTable("FavoriteItems", Constants.Schemas.App);
        builder.HasKey(m => m.Id);

        builder.Property(m => m.UserId).IsRequired().HasMaxLength(450);
        builder.Property(m => m.Path).IsRequired().HasMaxLength(700).IsUnicode(true);
        builder.Property(m => m.Label).HasMaxLength(256).IsUnicode(true);

        builder.HasIndex(m => new { m.UserId, m.SourceId, m.Path }).IsUnique();

        builder.HasOne(m => m.Source)
            .WithMany()
            .HasForeignKey(m => m.SourceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
