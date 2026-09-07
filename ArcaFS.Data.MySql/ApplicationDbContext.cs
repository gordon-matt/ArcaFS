namespace ArcaFS.Data.MySql;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : ApplicationDbContextBase(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // MySQL has no schema concept separate from the database, so the shared
        // HasDefaultSchema("app") would otherwise produce table names like `app.Sources`.
        builder.Model.SetDefaultSchema(null);
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            entityType.SetSchema(null);
        }
    }
}
