namespace ArcaFS.Data.Entities;

/// <summary>
/// A long-lived credential for WebDAV / scripted access. Only the hash is stored, so the plain
/// token is shown once at creation and cannot be recovered.
/// </summary>
public class ApiToken : BaseEntity<int>
{
    public required string UserId { get; set; }

    public required string Name { get; set; }

    /// <summary>SHA-256 of the token. Lookup hashes the presented value and matches on this.</summary>
    public required string TokenHash { get; set; }

    /// <summary>First few characters of the token, purely so the UI can identify a row.</summary>
    public required string TokenPrefix { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ExpiresAt { get; set; }

    public DateTime? LastUsedAt { get; set; }

    public bool IsRevoked { get; set; }
}

public class ApiTokenMap : IEntityTypeConfiguration<ApiToken>
{
    public void Configure(EntityTypeBuilder<ApiToken> builder)
    {
        builder.ToTable("ApiTokens", Constants.Schemas.App);
        builder.HasKey(m => m.Id);

        builder.Property(m => m.UserId).IsRequired().HasMaxLength(450);
        builder.Property(m => m.Name).IsRequired().HasMaxLength(128).IsUnicode(true);
        builder.Property(m => m.TokenHash).IsRequired().HasMaxLength(128);
        builder.Property(m => m.TokenPrefix).IsRequired().HasMaxLength(16);

        builder.HasIndex(m => m.TokenHash).IsUnique();
        builder.HasIndex(m => m.UserId);
    }
}
