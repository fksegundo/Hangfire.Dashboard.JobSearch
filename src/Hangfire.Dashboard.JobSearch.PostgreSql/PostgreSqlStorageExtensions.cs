using System;

namespace Hangfire.Dashboard.JobSearch;

public static class PostgreSqlStorageExtensions
{
    public static void UsePostgreSqlStorage(
        this JobSearchOptions options,
        string connectionString,
        PostgreSqlJobSearchOptions? storageOptions = null)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException("Connection string is required.", nameof(connectionString));

        options.UseStorage(_ => new PostgreSqlJobSearchStorage(connectionString, storageOptions));
    }

    public static void UsePostgreSqlStorage(
        this JobSearchOptions options,
        Func<IServiceProvider, string> connectionStringFactory,
        PostgreSqlJobSearchOptions? storageOptions = null)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (connectionStringFactory is null) throw new ArgumentNullException(nameof(connectionStringFactory));

        options.UseStorage(provider => new PostgreSqlJobSearchStorage(connectionStringFactory(provider), storageOptions));
    }
}
