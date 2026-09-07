namespace ArcaFS.Data.Entities;

/// <summary>Key/value store for instance-wide settings (theme default, setup state, toggles).</summary>
public class ServerSetting : BaseEntity<int>
{
    public required string Key { get; set; }

    public string? Value { get; set; }

    /// <summary>
    /// When the value last changed. Answers "who changed the theme?" style questions in
    /// combination with the activity log.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class ServerSettingMap : IEntityTypeConfiguration<ServerSetting>
{
    public void Configure(EntityTypeBuilder<ServerSetting> builder)
    {
        builder.ToTable("ServerSettings", Constants.Schemas.App);
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Key).IsRequired().HasMaxLength(Constants.MaxLengths.SettingKey);
        builder.Property(m => m.Value).HasMaxLength(4000).IsUnicode(true);

        builder.HasIndex(m => m.Key).IsUnique();
    }
}
