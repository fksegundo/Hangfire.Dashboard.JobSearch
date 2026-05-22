using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace Hangfire.Dashboard.JobSearch;

public sealed class RedisJobSearchStorage : IJobSearchStorage
{
    private readonly IConnectionMultiplexer _connection;
    private readonly string _prefix;
    private readonly TimeSpan? _expiry;

    public RedisJobSearchStorage(IConnectionMultiplexer connection, string keyPrefix = "hangfire:jobsearch", TimeSpan? expiry = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _prefix = keyPrefix ?? throw new ArgumentNullException(nameof(keyPrefix));
        _expiry = expiry;
    }

    private IDatabase Db => _connection.GetDatabase();
    private string MetaKey(string jobId) => $"{_prefix}:meta:{jobId}";
    private string IdxKey => $"{_prefix}:idx";

    public async Task SaveMetadataAsync(string jobId, JobSearchMetadata metadata, CancellationToken cancellationToken = default)
    {
        var db = Db;
        var metaKey = MetaKey(jobId);

        var hash = new List<HashEntry>
        {
            new HashEntry("jobId", metadata.JobId),
            new HashEntry("jobType", metadata.JobType),
            new HashEntry("methodName", metadata.MethodName),
            new HashEntry("state", metadata.State),
            new HashEntry("createdAt", metadata.CreatedAt.ToUnixTimeMilliseconds().ToString()),
        };

        foreach (var field in metadata.Fields)
            hash.Add(new HashEntry($"f:{field.Key}", field.Value ?? string.Empty));

        await db.HashSetAsync(metaKey, hash.ToArray());

        if (_expiry.HasValue)
            await db.KeyExpireAsync(metaKey, _expiry);

        var score = (double)metadata.CreatedAt.ToUnixTimeMilliseconds();
        await db.SortedSetAddAsync(IdxKey, jobId, score);
    }

    public async Task UpdateStateAsync(string jobId, string state, CancellationToken cancellationToken = default)
    {
        var db = Db;
        var metaKey = MetaKey(jobId);

        if (!await db.KeyExistsAsync(metaKey))
            return;

        await db.HashSetAsync(metaKey, "state", state);

        if (_expiry.HasValue)
            await db.KeyExpireAsync(metaKey, _expiry);
    }

    public async Task DeleteMetadataAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var db = Db;
        await db.KeyDeleteAsync(MetaKey(jobId));
        await db.SortedSetRemoveAsync(IdxKey, jobId);
    }

    public async Task<JobSearchResultPage> SearchAsync(JobSearchQuery query, CancellationToken cancellationToken = default)
    {
        var allMatching = await LoadMatchingAsync(query);
        var sorted = ApplySort(allMatching, query).ToList();

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? 20 : query.PageSize;

        var items = sorted
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToResult)
            .ToList();

        return new JobSearchResultPage
        {
            Items = items,
            TotalCount = sorted.Count,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<IReadOnlyDictionary<string, int>> CountByStateAsync(JobSearchQuery query, CancellationToken cancellationToken = default)
    {
        var stateAgnosticQuery = new JobSearchQuery
        {
            JobId = query.JobId,
            JobType = query.JobType,
            State = null,
            Filters = query.Filters,
            Page = 1,
            PageSize = int.MaxValue,
        };

        var allMatching = await LoadMatchingAsync(stateAgnosticQuery);

        return allMatching
            .GroupBy(m => m.State)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    private async Task<List<JobSearchMetadata>> LoadMatchingAsync(JobSearchQuery query)
    {
        var db = Db;
        var jobIds = await db.SortedSetRangeByRankAsync(IdxKey, 0, -1, Order.Descending);

        var results = new List<JobSearchMetadata>();
        foreach (var entry in jobIds)
        {
            var jobId = (string?)entry;
            if (jobId is null) continue;

            var hash = await db.HashGetAllAsync(MetaKey(jobId));
            if (hash.Length == 0) continue;

            var metadata = Deserialize(hash);
            if (Matches(metadata, query))
                results.Add(metadata);
        }

        return results;
    }

    private static JobSearchMetadata Deserialize(HashEntry[] hash)
    {
        var dict = new Dictionary<string, string?>(StringComparer.Ordinal);
        var fields = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var entry in hash)
        {
            var name = (string?)entry.Name;
            var value = (string?)entry.Value;
            if (name is null) continue;

            if (name.StartsWith("f:", StringComparison.Ordinal))
                fields[name.Substring(2)] = value;
            else
                dict[name] = value;
        }

        string? GetField(string key)
        {
            dict.TryGetValue(key, out var v);
            return v;
        }

        var createdAt = long.TryParse(GetField("createdAt"), out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : DateTimeOffset.UtcNow;

        return new JobSearchMetadata
        {
            JobId = GetField("jobId") ?? string.Empty,
            JobType = GetField("jobType") ?? string.Empty,
            MethodName = GetField("methodName") ?? string.Empty,
            State = GetField("state") ?? string.Empty,
            CreatedAt = createdAt,
            Fields = fields,
        };
    }

    private static bool Matches(JobSearchMetadata metadata, JobSearchQuery query)
    {
        if (query.JobId is not null && !string.Equals(metadata.JobId, query.JobId, StringComparison.OrdinalIgnoreCase))
            return false;

        if (query.State is not null && !string.Equals(metadata.State, query.State, StringComparison.OrdinalIgnoreCase))
            return false;

        if (query.JobType is not null && !string.Equals(metadata.JobType, query.JobType, StringComparison.OrdinalIgnoreCase))
            return false;

        foreach (var filter in query.Filters)
        {
            if (!metadata.Fields.TryGetValue(filter.Key, out var fieldValue))
                return false;
            if (!string.Equals(fieldValue, filter.Value, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static IEnumerable<JobSearchMetadata> ApplySort(IEnumerable<JobSearchMetadata> items, JobSearchQuery query)
    {
        var sorts = query.Sorts.Count > 0
            ? query.Sorts
            : query.SortBy is not null
                ? new List<JobSearchSort> { new JobSearchSort { Field = query.SortBy, Descending = query.SortDescending } }
                : new List<JobSearchSort> { new JobSearchSort { Field = "createdAt", Descending = true } };

        IOrderedEnumerable<JobSearchMetadata>? ordered = null;
        foreach (var sort in sorts)
        {
            var capturedSort = sort;
            Func<JobSearchMetadata, string> key = capturedSort.Field.ToLowerInvariant() switch
            {
                "jobid" => m => m.JobId,
                "state" => m => m.State,
                "jobtype" => m => m.JobType,
                "methodname" => m => m.MethodName,
                "createdat" => m => m.CreatedAt.ToUnixTimeMilliseconds().ToString("D20"),
                _ => m => m.Fields.TryGetValue(capturedSort.Field, out var v) ? v ?? string.Empty : string.Empty,
            };

            if (ordered is null)
                ordered = capturedSort.Descending ? items.OrderByDescending(key) : items.OrderBy(key);
            else
                ordered = capturedSort.Descending ? ordered.ThenByDescending(key) : ordered.ThenBy(key);
        }

        return ordered ?? items;
    }

    private static JobSearchResult ToResult(JobSearchMetadata m) => new JobSearchResult
    {
        JobId = m.JobId,
        JobType = m.JobType,
        MethodName = m.MethodName,
        State = m.State,
        CreatedAt = m.CreatedAt,
        Fields = m.Fields,
    };
}
