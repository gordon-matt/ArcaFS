using System.Transactions;
using Hangfire;
using Hangfire.Storage.MySql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using LogVue.Data.MySql;

namespace ArcaFS.Data.MySql;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddArcaFSMySql(IConfiguration configuration)
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
                    options.UseMySQL(connectionString, mySql =>
                        mySql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.GetName().Name));
                }
            });

            services.AddScoped<ApplicationDbContextBase>(sp => sp.GetRequiredService<ApplicationDbContext>());
            services.AddSingleton<IDbContextFactory, ApplicationDbContextFactory>();

            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                services.AddLogVueMySql(connectionString);
            }

            return services;
        }

        /// <summary>
        /// Hangfire.Storage.MySql needs <c>Allow User Variables=True</c> in the connection string;
        /// without it the storage bootstrap fails with a syntax error on its first statement.
        /// </summary>
        public IServiceCollection AddArcaFSMySqlHangfire(string connectionString)
        {
            var storage = new MySqlStorage(connectionString, new MySqlStorageOptions
            {
                TransactionIsolationLevel = IsolationLevel.ReadCommitted,
                QueuePollInterval = TimeSpan.FromSeconds(15),
                JobExpirationCheckInterval = TimeSpan.FromHours(1),
                CountersAggregateInterval = TimeSpan.FromMinutes(5),
                PrepareSchemaIfNecessary = true,
                DashboardJobListLimit = 50000,
                TransactionTimeout = TimeSpan.FromMinutes(1),
                TablesPrefix = "Hangfire",
            });

            services.AddHangfire(config => config
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UseStorage(storage));

            return services;
        }
    }
}
