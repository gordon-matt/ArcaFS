using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace ArcaFS.Data;

/// <summary>
/// Provider-agnostic context. Each <c>ArcaFS.Data.{Provider}</c> project subclasses this and owns
/// its own migration history, because EF bakes provider-specific column types into migrations.
/// </summary>
public abstract class ApplicationDbContextBase
    : IdentityDbContext<ApplicationUser, ApplicationRole, string>
{
    protected ApplicationDbContextBase(DbContextOptions options)
        : base(options)
    {
    }

    public DbSet<Source> Sources => Set<Source>();

    public DbSet<AccessRule> AccessRules => Set<AccessRule>();

    public DbSet<AccessGroup> AccessGroups => Set<AccessGroup>();

    public DbSet<AccessGroupMember> AccessGroupMembers => Set<AccessGroupMember>();

    public DbSet<IndexedItem> IndexedItems => Set<IndexedItem>();

    public DbSet<Share> Shares => Set<Share>();

    public DbSet<ServerSetting> ServerSettings => Set<ServerSetting>();

    public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();

    public DbSet<FavoriteItem> FavoriteItems => Set<FavoriteItem>();

    public DbSet<ApiToken> ApiTokens => Set<ApiToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasDefaultSchema(Constants.Schemas.App);

        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContextBase).Assembly);

        ConfigureIdentityTables(builder);
    }

    /// <summary>
    /// Renames the Identity tables into the <c>app</c> schema so a deployment doesn't end up with
    /// <c>AspNetUsers</c> sitting next to <c>app.Sources</c>.
    /// </summary>
    protected static void ConfigureIdentityTables(ModelBuilder builder)
    {
        builder.Entity<ApplicationUser>().ToTable("Users", Constants.Schemas.App);
        builder.Entity<ApplicationRole>().ToTable("Roles", Constants.Schemas.App);
        builder.Entity<IdentityUserRole<string>>().ToTable("UserRoles", Constants.Schemas.App);
        builder.Entity<IdentityUserClaim<string>>().ToTable("UserClaims", Constants.Schemas.App);
        builder.Entity<IdentityUserLogin<string>>().ToTable("UserLogins", Constants.Schemas.App);
        builder.Entity<IdentityUserToken<string>>().ToTable("UserTokens", Constants.Schemas.App);
        builder.Entity<IdentityRoleClaim<string>>().ToTable("RoleClaims", Constants.Schemas.App);
    }
}