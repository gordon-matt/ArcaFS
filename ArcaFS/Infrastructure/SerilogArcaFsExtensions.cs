using Serilog;
using Serilog.Configuration;
using Serilog.Events;
using Serilog.Sinks.MSSqlServer;
using Serilog.Sinks.PostgreSQL.ColumnWriters;

namespace ArcaFS.Infrastructure;

public static class SerilogArcaFsExtensions
{
    private const string LogTableName = "Log";

    /// <summary>
    /// Adds a relational sink matching <c>Database:Provider</c>, so logs land in the same
    /// database the app uses and Sejil's dashboard can read them back.
    /// </summary>
    /// <remarks>
    /// Configured in code rather than <c>appsettings.json</c> because the sink package differs
    /// per provider; expressing that in configuration would mean every deployment carrying
    /// sink settings for three databases it isn't using.
    /// </remarks>
    public static LoggerConfiguration ArcaFsDatabase(
        this LoggerSinkConfiguration sinkConfiguration,
        IConfiguration configuration)
    {
        string provider = ArcaFsConfiguration.ResolveDatabaseProvider(configuration);
        string? connectionString = configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // No connection string means the in-memory provider is in use (tests, or a first
            // run before configuration). Console logging alone is correct there.
            return sinkConfiguration.Sink(new NullSink());
        }

        return provider switch
        {
            Constants.DatabaseProviders.Sqlite => sinkConfiguration.SQLite(
                sqliteDbPath: ResolveSqlitePath(connectionString),
                tableName: LogTableName,
                restrictedToMinimumLevel: LogEventLevel.Information),

            Constants.DatabaseProviders.SqlServer => sinkConfiguration.MSSqlServer(
                connectionString: connectionString,
                sinkOptions: new MSSqlServerSinkOptions
                {
                    TableName = LogTableName,
                    AutoCreateSqlTable = true,
                },
                restrictedToMinimumLevel: LogEventLevel.Information),

            // The null cast picks between two overloads that differ only in the type of a
            // dictionary parameter we aren't supplying.
            Constants.DatabaseProviders.Npgsql => sinkConfiguration.PostgreSQL(
                connectionString: connectionString,
                tableName: LogTableName,
                columnOptions: (IDictionary<string, ColumnWriterBase>?)null,
                needAutoCreateTable: true,
                restrictedToMinimumLevel: LogEventLevel.Information),

            Constants.DatabaseProviders.MySql => sinkConfiguration.MySQL(
                connectionString: connectionString,
                tableName: LogTableName,
                restrictedToMinimumLevel: LogEventLevel.Information),

            _ => sinkConfiguration.Sink(new NullSink()),
        };
    }

    /// <summary>
    /// Pulls the file path out of a SQLite connection string, since the sink wants a path rather
    /// than a connection string.
    /// </summary>
    private static string ResolveSqlitePath(string connectionString)
    {
        foreach (string part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);

            if (pair.Length == 2
                && (pair[0].Trim().Equals("Data Source", StringComparison.OrdinalIgnoreCase)
                    || pair[0].Trim().Equals("Filename", StringComparison.OrdinalIgnoreCase)))
            {
                return pair[1].Trim();
            }
        }

        // Already a bare path.
        return connectionString;
    }

    /// <summary>Discards events. Used when no database sink applies.</summary>
    private sealed class NullSink : Serilog.Core.ILogEventSink
    {
        public void Emit(LogEvent logEvent)
        {
        }
    }
}
