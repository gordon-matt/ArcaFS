using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using LogVue.Data.Npgsql;

namespace ArcaFS.Data.Npgsql;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddArcaFSNpgsql(IConfiguration configuration)
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
                    options.UseNpgsql(connectionString, npgsql =>
                        npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.GetName().Name));
                }
            });

            services.AddScoped<ApplicationDbContextBase>(sp => sp.GetRequiredService<ApplicationDbContext>());
            services.AddSingleton<IDbContextFactory, ApplicationDbContextFactory>();

            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                services.AddLogVueNpgsql(connectionString);
            }

            return services;
        }

        public IServiceCollection AddArcaFSNpgsqlHangfire(string connectionString)
        {
            services.AddHangfire(config => config
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString)));

            return services;
        }
    }
}
