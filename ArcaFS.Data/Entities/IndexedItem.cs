namespace ArcaFS.Data.Entities;

/// <summary>
/// One file or folder recorded by the background indexer. Search, the duplicate finder and the
/// folder-size figures all read from here rather than walking the disk, so a query over a large
/// tree costs a single indexed lookup.
/// <para>
/// The row is a cache, not a source of truth: anything missing or stale is corrected by the next
/// scan, and listings always read the live filesystem.
/// </para>
/// </summary>
public class IndexedItem : BaseEntity<long>
{
    public int SourceId { get; set; }

    /// <summary>Normalized virtual path of the item itself.</summary>
    public required string Path { get; set; }

    /// <summary>
    /// Normalized virtual path of the containing folder. Denormalised so listing one directory
    /// is an equality match instead of a prefix scan.
    /// </summary>
    public required string ParentPath { get; set; }

    public required string Name { get; set; }

    /// <summary>Lower-cased <see cref="Name"/>. Gives case-insensitive search on every provider.</summary>
    public required string NameLower { get; set; }

    public string? Extension { get; set; }

    public bool IsDirectory { get; set; }

    public bool IsHidden { get; set; }

    /// <summary>File length, or the recursive total for a directory.</summary>
    public long SizeBytes { get; set; }

    public DateTime ModifiedAt { get; set; }

    public FileKind Kind { get; set; }

    /// <summary>Depth below the source root, so <c>type:folder</c> style filters can bound a search.</summary>
    public int Depth { get; set; }

    /// <summary>Immediate child count for directories.</summary>
    public int ChildCount { get; set; }

    /// <summary>
    /// Content hash, populated lazily by the duplicate finder rather than during the scan —
    /// hashing every file on every pass would make scanning unusable on large sources.
    /// </summary>
    public string? ContentHash { get; set; }

    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    public virtual Source Source { get; set; } = null!;
}

public class IndexedItemMap : IEntityTypeConfiguration<IndexedItem>
{
    public void Configure(EntityTypeBuilder<IndexedItem> builder)
    {
        builder.ToTable("IndexedItems", Constants.Schemas.App);
        builder.HasKey(m => m.Id);

        // Path columns are capped well under the 767-byte InnoDB key limit so the composite
        // indexes below build on MySQL without a provider-specific override.
        builder.Property(m => m.Path).IsRequired().HasMaxLength(700).IsUnicode(true);
        builder.Property(m => m.ParentPath).IsRequired().HasMaxLength(700).IsUnicode(true);
        builder.Property(m => m.Name).IsRequired().HasMaxLength(512).IsUnicode(true);
        builder.Property(m => m.NameLower).IsRequired().HasMaxLength(512).IsUnicode(true);
        builder.Property(m => m.Extension).HasMaxLength(32);
        builder.Property(m => m.ContentHash).HasMaxLength(64);
        builder.Property(m => m.Kind).HasConversion<int>();

        builder.HasIndex(m => new { m.SourceId, m.Path }).IsUnique();
        builder.HasIndex(m => new { m.SourceId, m.ParentPath });
        builder.HasIndex(m => new { m.SourceId, m.NameLower });
        builder.HasIndex(m => new { m.SourceId, m.Kind });
        builder.HasIndex(m => new { m.SourceId, m.SizeBytes });
        builder.HasIndex(m => m.ContentHash);

        builder.HasOne(m => m.Source)
            .WithMany(m => m.IndexedItems)
            .HasForeignKey(m => m.SourceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
