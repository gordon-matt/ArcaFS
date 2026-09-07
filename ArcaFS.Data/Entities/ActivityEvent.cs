namespace ArcaFS.Data.Entities;

/// <summary>
/// Audit trail of who did what to which path. Separate from the Serilog <c>Log</c> table: this
/// is queryable domain history surfaced in the UI, not diagnostics.
/// </summary>
public class ActivityEvent : BaseEntity<long>
{
    public ActivityKind Kind { get; set; }

    public string? UserId { get; set; }

    public string? UserName { get; set; }

    public int? SourceId { get; set; }

    public string? Path { get; set; }

    /// <summary>Destination for move/copy/rename, otherwise null.</summary>
    public string? TargetPath { get; set; }

    /// <summary>Share token when the action came in through a public link.</summary>
    public string? ShareToken { get; set; }

    public long? SizeBytes { get; set; }

    public bool Succeeded { get; set; } = true;

    /// <summary>Failure reason, or the denied permission for <see cref="ActivityKind.PermissionDenied"/>.</summary>
    public string? Detail { get; set; }

    public string? IpAddress { get; set; }

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}

public class ActivityEventMap : IEntityTypeConfiguration<ActivityEvent>
{
    public void Configure(EntityTypeBuilder<ActivityEvent> builder)
    {
        builder.ToTable("ActivityEvents", Constants.Schemas.App);
        builder.HasKey(m => m.Id);

        builder.Property(m => m.UserId).HasMaxLength(450);
        builder.Property(m => m.UserName).HasMaxLength(256).IsUnicode(true);
        builder.Property(m => m.Path).HasMaxLength(700).IsUnicode(true);
        builder.Property(m => m.TargetPath).HasMaxLength(700).IsUnicode(true);
        builder.Property(m => m.ShareToken).HasMaxLength(Constants.MaxLengths.ShareToken);
        builder.Property(m => m.Detail).HasMaxLength(1024).IsUnicode(true);
        builder.Property(m => m.IpAddress).HasMaxLength(64);
        builder.Property(m => m.Kind).HasConversion<int>();

        builder.HasIndex(m => m.OccurredAt);
        builder.HasIndex(m => new { m.UserId, m.OccurredAt });
        builder.HasIndex(m => new { m.SourceId, m.OccurredAt });
    }
}
