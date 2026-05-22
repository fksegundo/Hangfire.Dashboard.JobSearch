using System;
using StackExchange.Redis;

namespace Hangfire.Dashboard.JobSearch;

public static class RedisStorageExtensions
{
    public static void UseRedisStorage(
        this JobSearchOptions options,
        IConnectionMultiplexer connection,
        string keyPrefix = "hangfire:jobsearch",
        TimeSpan? expiry = null)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (connection is null) throw new ArgumentNullException(nameof(connection));

        options.UseStorage(_ => new RedisJobSearchStorage(connection, keyPrefix, expiry));
    }

    public static void UseRedisStorage(
        this JobSearchOptions options,
        Func<IServiceProvider, IConnectionMultiplexer> connectionFactory,
        string keyPrefix = "hangfire:jobsearch",
        TimeSpan? expiry = null)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (connectionFactory is null) throw new ArgumentNullException(nameof(connectionFactory));

        options.UseStorage(provider => new RedisJobSearchStorage(connectionFactory(provider), keyPrefix, expiry));
    }
}
