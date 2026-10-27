using Microsoft.EntityFrameworkCore.Design;

namespace ArcaFS.Data.Npgsql;

/// <summary>
/// Used by <c>dotnet ef</c> so migrations can be generated without a running PostgreSQL instance.
/// </summary>
public class ApplicationDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Database=arcafs_design;Username=postgres;Password=design",
            npgsql => npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.GetName().Name));
        return new ApplicationDbContext(optionsBuilder.Options);
    }
}