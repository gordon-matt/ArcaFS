using Hangfire;
using Hangfire.Storage.SQLite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ArcaFS.Data.Sqlite;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddArcaFSSqlite(IConfiguration configuration)
        {
            string? connectionString = configuration.GetConnectionString("DefaultConnection");

            services.AddDbContext<ApplicationDbContext>(options =>
            {
                if (string.IsNullOrEmpty(connectionString))
                {
                    options.UseInMemoryDatabase("ArcaFSDb");
                }
                else
                {
                    options.UseSqlite(connectionString, sqlite =>
                        sqlite.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.GetName().Name));
                    options.ConfigureWarnings(w => w.Ignore(SqliteEventId.SchemaConfiguredWarning));
                }
            });

            // Everything above the data layer resolves the abstract base, so services stay
            // provider-agnostic and the concrete context is an implementation detail.
            services.AddScoped<ApplicationDbContextBase>(sp => sp.GetRequiredService<ApplicationDbContext>());
            services.AddSingleton<IDbContextFactory, ApplicationDbContextFactory>();

            return services;
        }

        /// <summary>
        /// Hangfire.Storage.SQLite wants its own file: sharing one SQLite database between EF and
        /// Hangfire's polling means constant write-lock contention.
        /// </summary>
        public IServiceCollection AddArcaFSSqliteHangfire(string? hangfireDbPath = null)
        {
            string path = string.IsNullOrWhiteSpace(hangfireDbPath) ? "hangfire.db" : hangfireDbPath;

            services.AddHangfire(config => config
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UseSQLiteStorage(path));

            return services;
        }
    }
}
