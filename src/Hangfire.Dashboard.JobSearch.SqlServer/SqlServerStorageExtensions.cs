using System;

namespace Hangfire.Dashboard.JobSearch;

public static class SqlServerStorageExtensions
{
    public static void UseSqlServerStorage(
        this JobSearchOptions options,
        string connectionString,
        SqlServerJobSearchOptions? storageOptions = null)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException("Connection string is required.", nameof(connectionString));

        options.UseStorage(_ => new SqlServerJobSearchStorage(connectionString, storageOptions));
    }

    public static void UseSqlServerStorage(
        this JobSearchOptions options,
        Func<IServiceProvider, string> connectionStringFactory,
        SqlServerJobSearchOptions? storageOptions = null)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (connectionStringFactory is null) throw new ArgumentNullException(nameof(connectionStringFactory));

        options.UseStorage(provider => new SqlServerJobSearchStorage(connectionStringFactory(provider), storageOptions));
    }
}
