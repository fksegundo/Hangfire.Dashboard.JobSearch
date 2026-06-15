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
    private readonly int _maxSortWindow;

    private static readonly string[] KnownHangfireStates =
        new[] { "Enqueued", "Processing", "Succeeded", "Failed", "Deleted", "Scheduled", "Awaiting" };

    // KEYS[1]=metaHash  KEYS[2]=newStateIdx
    // ARGV[1]=jobId  ARGV[2]=newState  ARGV[3]=stateIdxPrefix
    private static readonly string MoveStateScript =
        "if redis.call('EXISTS', KEYS[1]) == 0 then return 0 end\n" +
        "local old = redis.call('HGET', KEYS[1], 'state')\n" +
        "local score = tonumber(redis.call('HGET', KEYS[1], 'createdAt')) or 0\n" +
        "if old then redis.call('ZREM', ARGV[3] .. string.lower(tostring(old)), ARGV[1]) end\n" +
        "redis.call('ZADD', KEYS[2], score, ARGV[1])\n" +
        "redis.call('HSET', KEYS[1], 'state', ARGV[2])\n" +
        "return 1";

    public RedisJobSearchStorage(
        IConnectionMultiplexer connection,
        string keyPrefix = "hangfire:jobsearch",
        TimeSpan? expiry = null,
        int maxSortWindow = 5000)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _prefix = keyPrefix ?? throw new ArgumentNullException(nameof(keyPrefix));
        _expiry = expiry;
        _maxSortWindow = maxSortWindow > 0 ? maxSortWindow : 5000;
    }

    private IDatabase Db => _connection.GetDatabase();

    private string V2MetaKey(string jobId) => $"{_prefix}:v2:meta:{jobId}";
    private string V2GlobalIdx => $"{_prefix}:v2:idx";
    private string V2StateIdx(string state) => $"{_prefix}:v2:idx:state:{state.ToLowerInvariant()}";
    private string V2TypeIdx(string type) => $"{_prefix}:v2:idx:type:{type.ToLowerInvariant()}";
    private string V2FieldIdx(string field, string value) => $"{_prefix}:v2:idx:f:{field.ToLowerInvariant()}:{value.ToLowerInvariant()}";
    private string V2RevIdx(string jobId) => $"{_prefix}:v2:rev:{jobId}";
    private string V2StateIdxPrefix => $"{_prefix}:v2:idx:state:";
    private string V2TmpKey() => $"{_prefix}:v2:tmp:{Guid.NewGuid():N}";

    public async Task SaveMetadataAsync(string jobId, JobSearchMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(jobId)) throw new ArgumentException("Job id is required.", nameof(jobId));
        if (metadata is null) throw new ArgumentNullException(nameof(metadata));

        var db = Db;
        var metaKey = V2MetaKey(jobId);
        var score = (double)metadata.CreatedAt.ToUnixTimeMilliseconds();

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

        var indexKeys = new List<RedisValue>
        {
            (RedisValue)V2GlobalIdx,
            (RedisValue)V2StateIdx(metadata.State),
            (RedisValue)V2TypeIdx(metadata.JobType),
        };
        foreach (var field in metadata.Fields)
            if (!string.IsNullOrEmpty(field.Value))
                indexKeys.Add((RedisValue)V2FieldIdx(field.Key, field.Value!));

        var revKey = V2RevIdx(jobId);
        var batch = db.CreateBatch();
        var tasks = new List<Task>();

        tasks.Add(batch.HashSetAsync(metaKey, hash.ToArray()));
        tasks.Add(batch.SortedSetAddAsync(V2GlobalIdx, jobId, score));
        tasks.Add(batch.SortedSetAddAsync(V2StateIdx(metadata.State), jobId, score));
        tasks.Add(batch.SortedSetAddAsync(V2TypeIdx(metadata.JobType), jobId, score));
        tasks.Add(batch.SetAddAsync(revKey, indexKeys.ToArray()));

        foreach (var field in metadata.Fields)
            if (!string.IsNullOrEmpty(field.Value))
                tasks.Add(batch.SortedSetAddAsync(V2FieldIdx(field.Key, field.Value!), jobId, score));

        if (_expiry.HasValue)
        {
            tasks.Add(batch.KeyExpireAsync(metaKey, _expiry));
            // Reverse index outlives the hash by 1 day to allow cleanup of already-expired entries
            tasks.Add(batch.KeyExpireAsync(revKey, _expiry.Value.Add(TimeSpan.FromDays(1))));
        }

        batch.Execute();
        await Task.WhenAll(tasks);
    }

    public async Task UpdateStateAsync(string jobId, string state, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(jobId)) throw new ArgumentException("Job id is required.", nameof(jobId));
        if (string.IsNullOrEmpty(state)) throw new ArgumentException("State is required.", nameof(state));

        var db = Db;
        var metaKey = V2MetaKey(jobId);

        await db.ScriptEvaluateAsync(
            MoveStateScript,
            keys: new RedisKey[] { metaKey, V2StateIdx(state) },
            values: new RedisValue[] { jobId, state, V2StateIdxPrefix });

        if (_expiry.HasValue)
            await db.KeyExpireAsync(metaKey, _expiry);
    }

    public async Task DeleteMetadataAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(jobId)) throw new ArgumentException("Job id is required.", nameof(jobId));

        var db = Db;
        var metaKey = V2MetaKey(jobId);
        var revKey = V2RevIdx(jobId);

        var hash = await db.HashGetAllAsync(metaKey);
        if (hash.Length == 0)
        {
            // Hash already expired — use reverse index to clean up any remaining ZSET memberships
            var revMembers = await db.SetMembersAsync(revKey);
            if (revMembers.Length > 0)
            {
                var batch = db.CreateBatch();
                var tasks = new List<Task>();
                foreach (var k in revMembers)
                    tasks.Add(batch.SortedSetRemoveAsync((RedisKey)(string)k!, jobId));
                tasks.Add(batch.KeyDeleteAsync(revKey));
                batch.Execute();
                await Task.WhenAll(tasks);
            }
            return;
        }

        var meta = Deserialize(hash);

        var batch2 = db.CreateBatch();
        var tasks2 = new List<Task>();

        tasks2.Add(batch2.KeyDeleteAsync(metaKey));
        tasks2.Add(batch2.KeyDeleteAsync(revKey));
        tasks2.Add(batch2.SortedSetRemoveAsync(V2GlobalIdx, jobId));
        tasks2.Add(batch2.SortedSetRemoveAsync(V2StateIdx(meta.State), jobId));
        tasks2.Add(batch2.SortedSetRemoveAsync(V2TypeIdx(meta.JobType), jobId));

        foreach (var field in meta.Fields)
            if (!string.IsNullOrEmpty(field.Value))
                tasks2.Add(batch2.SortedSetRemoveAsync(V2FieldIdx(field.Key, field.Value!), jobId));

        batch2.Execute();
        await Task.WhenAll(tasks2);
    }

    public async Task<JobSearchResultPage> SearchAsync(JobSearchQuery query, CancellationToken cancellationToken = default)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));

        var db = Db;
        var page = Math.Max(1, query.Page);
        var pageSize = query.PageSize <= 0 ? 20 : query.PageSize;

        if (!string.IsNullOrEmpty(query.JobId))
            return await SearchByJobIdAsync(db, query, page, pageSize);

        var filterKeys = ResolveFilterKeys(query);

        RedisKey workingKey;
        string? tmpKey = null;

        if (filterKeys.Count == 0)
        {
            workingKey = (RedisKey)V2GlobalIdx;
        }
        else if (filterKeys.Count == 1)
        {
            workingKey = filterKeys[0];
        }
        else
        {
            tmpKey = V2TmpKey();
            await db.SortedSetCombineAndStoreAsync(
                SetOperation.Intersect, (RedisKey)tmpKey, filterKeys.ToArray(), aggregate: Aggregate.Min);
            await db.KeyExpireAsync(tmpKey, TimeSpan.FromSeconds(30));
            workingKey = (RedisKey)tmpKey;
        }

        try
        {
            var total = (int)await db.SortedSetLengthAsync(workingKey);
            if (total == 0)
                return EmptyPage(page, pageSize);

            if (IsCreatedAtSort(query))
            {
                var order = GetSortDirection(query);
                var start = (long)(page - 1) * pageSize;
                var stop = (long)page * pageSize - 1;
                var jobIds = await db.SortedSetRangeByRankAsync(workingKey, start, stop, order);
                var items = await LoadPageItemsAsync(db, jobIds, workingKey);
                return new JobSearchResultPage { Items = items, TotalCount = total, Page = page, PageSize = pageSize };
            }
            else
            {
                var windowSize = Math.Min(total, _maxSortWindow);
                var windowIds = await db.SortedSetRangeByRankAsync(workingKey, 0, windowSize - 1, Order.Descending);
                var allMeta = await LoadAllMetaAsync(db, windowIds, workingKey);
                var sorted = ApplySort(allMeta, query).ToList();
                var items = sorted.Skip((page - 1) * pageSize).Take(pageSize).Select(ToResult).ToList();
                return new JobSearchResultPage
                {
                    Items = items,
                    TotalCount = Math.Min(total, windowSize),
                    Page = page,
                    PageSize = pageSize,
                };
            }
        }
        finally
        {
            if (tmpKey != null)
                await db.KeyDeleteAsync(tmpKey);
        }
    }

    public async Task<IReadOnlyDictionary<string, int>> CountByStateAsync(JobSearchQuery query, CancellationToken cancellationToken = default)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));

        var db = Db;
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        if (!string.IsNullOrEmpty(query.JobId))
        {
            var hash = await db.HashGetAllAsync(V2MetaKey(query.JobId!));
            if (hash.Length == 0) return counts;
            var meta = Deserialize(hash);
            counts[meta.State] = 1;
            return counts;
        }

        var baseKeys = ResolveBaseFilterKeys(query);

        if (baseKeys.Count == 0)
        {
            var batch = db.CreateBatch();
            var stateTasks = new List<(string State, Task<long> Task)>();
            foreach (var state in KnownHangfireStates)
                stateTasks.Add((state, batch.SortedSetLengthAsync(V2StateIdx(state))));
            batch.Execute();

            foreach (var (state, task) in stateTasks)
            {
                var count = (int)await task;
                if (count > 0) counts[state] = count;
            }
        }
        else
        {
            var stateCounts = await Task.WhenAll(
                KnownHangfireStates.Select(s => CountStateWithFiltersAsync(db, baseKeys, s)));

            foreach (var (state, count) in stateCounts)
                if (count > 0) counts[state] = count;
        }

        return counts;
    }

    internal async Task<Dictionary<string, List<JobSearchResult>>> SearchChildrenBulkAsync(
        string childJobType,
        string childMethodName,
        string keyField,
        IReadOnlyList<string> parentKeys,
        int maxPerKey)
    {
        var db = Db;
        var result = new Dictionary<string, List<JobSearchResult>>(StringComparer.Ordinal);

        var distinctKeys = new List<string>();
        foreach (var key in parentKeys)
            if (!string.IsNullOrEmpty(key) && !distinctKeys.Contains(key))
                distinctKeys.Add(key);

        if (distinctKeys.Count == 0)
            return result;

        var batch1 = db.CreateBatch();
        var rangeTasks = new List<(string Key, Task<RedisValue[]> Task)>();
        foreach (var key in distinctKeys)
            rangeTasks.Add((key, batch1.SortedSetRangeByRankAsync(V2FieldIdx(keyField, key), 0, maxPerKey - 1, Order.Descending)));
        batch1.Execute();

        var allPairs = new List<(string ParentKey, string JobId)>();
        foreach (var (key, rangeTask) in rangeTasks)
        {
            var ids = await rangeTask;
            foreach (var id in ids)
            {
                var jobId = (string?)id;
                if (jobId != null)
                    allPairs.Add((key, jobId));
            }
        }

        if (allPairs.Count == 0)
            return result;

        var batch2 = db.CreateBatch();
        var hashTasks = new List<(string ParentKey, Task<HashEntry[]> Task)>();
        foreach (var (parentKey, jobId) in allPairs)
            hashTasks.Add((parentKey, batch2.HashGetAllAsync(V2MetaKey(jobId))));
        batch2.Execute();

        foreach (var (parentKey, hashTask) in hashTasks)
        {
            var hash = await hashTask;
            if (hash.Length == 0) continue;

            var meta = Deserialize(hash);
            if (!string.Equals(meta.JobType, childJobType, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrEmpty(childMethodName) &&
                !string.Equals(meta.MethodName, childMethodName, StringComparison.Ordinal)) continue;

            if (!result.TryGetValue(parentKey, out var list))
            {
                list = new List<JobSearchResult>();
                result[parentKey] = list;
            }
            list.Add(ToResult(meta));
        }

        return result;
    }

    private async Task<(string State, int Count)> CountStateWithFiltersAsync(
        IDatabase db,
        IReadOnlyList<RedisKey> baseKeys,
        string state)
    {
        var keys = new RedisKey[baseKeys.Count + 1];
        for (var i = 0; i < baseKeys.Count; i++) keys[i] = baseKeys[i];
        keys[baseKeys.Count] = (RedisKey)V2StateIdx(state);

        var tmpKey = (RedisKey)V2TmpKey();
        try
        {
            await db.SortedSetCombineAndStoreAsync(SetOperation.Intersect, tmpKey, keys, aggregate: Aggregate.Min);
            await db.KeyExpireAsync(tmpKey, TimeSpan.FromSeconds(5));
            return (state, (int)await db.SortedSetLengthAsync(tmpKey));
        }
        finally
        {
            await db.KeyDeleteAsync(tmpKey);
        }
    }

    private async Task<JobSearchResultPage> SearchByJobIdAsync(IDatabase db, JobSearchQuery query, int page, int pageSize)
    {
        var hash = await db.HashGetAllAsync(V2MetaKey(query.JobId!));
        if (hash.Length == 0)
            return EmptyPage(page, pageSize);

        var meta = Deserialize(hash);
        if (!Matches(meta, query))
            return EmptyPage(page, pageSize);

        return new JobSearchResultPage
        {
            Items = new List<JobSearchResult> { ToResult(meta) },
            TotalCount = 1,
            Page = page,
            PageSize = pageSize,
        };
    }

    private async Task<List<JobSearchResult>> LoadPageItemsAsync(IDatabase db, RedisValue[] jobIds, RedisKey sourceKey)
    {
        if (jobIds.Length == 0)
            return new List<JobSearchResult>();

        var batch = db.CreateBatch();
        var tasks = new List<(string? Id, Task<HashEntry[]> HashTask)>();
        foreach (var id in jobIds)
        {
            var jobId = (string?)id;
            if (jobId != null)
                tasks.Add((jobId, batch.HashGetAllAsync(V2MetaKey(jobId))));
        }
        batch.Execute();

        var items = new List<JobSearchResult>(tasks.Count);
        var stale = new List<string>();

        foreach (var (id, hashTask) in tasks)
        {
            var hash = await hashTask;
            if (hash.Length == 0)
            {
                if (id != null) stale.Add(id);
                continue;
            }
            items.Add(ToResult(Deserialize(hash)));
        }

        if (stale.Count > 0)
            _ = PruneStaleAsync(db, stale, sourceKey);

        return items;
    }

    private async Task<List<JobSearchMetadata>> LoadAllMetaAsync(IDatabase db, RedisValue[] jobIds, RedisKey sourceKey)
    {
        if (jobIds.Length == 0)
            return new List<JobSearchMetadata>();

        var batch = db.CreateBatch();
        var tasks = new List<(string? Id, Task<HashEntry[]> HashTask)>();
        foreach (var id in jobIds)
        {
            var jobId = (string?)id;
            if (jobId != null)
                tasks.Add((jobId, batch.HashGetAllAsync(V2MetaKey(jobId))));
        }
        batch.Execute();

        var results = new List<JobSearchMetadata>(tasks.Count);
        var stale = new List<string>();

        foreach (var (id, hashTask) in tasks)
        {
            var hash = await hashTask;
            if (hash.Length == 0)
            {
                if (id != null) stale.Add(id);
                continue;
            }
            results.Add(Deserialize(hash));
        }

        if (stale.Count > 0)
            _ = PruneStaleAsync(db, stale, sourceKey);

        return results;
    }

    private async Task PruneStaleAsync(IDatabase db, IReadOnlyList<string> staleIds, RedisKey sourceKey)
    {
        try
        {
            // Batch-read reverse index sets for all stale IDs
            var revBatch = db.CreateBatch();
            var revTasks = staleIds.Select(id => (id, revBatch.SetMembersAsync(V2RevIdx(id)))).ToList();
            revBatch.Execute();

            var removeBatch = db.CreateBatch();
            var removeTasks = new List<Task>();
            foreach (var (id, revTask) in revTasks)
            {
                var members = await revTask;
                if (members.Length > 0)
                {
                    // Full cleanup: remove from every ZSET the job was indexed into
                    foreach (var k in members)
                        removeTasks.Add(removeBatch.SortedSetRemoveAsync((RedisKey)(string)k!, id));
                    removeTasks.Add(removeBatch.KeyDeleteAsync(V2RevIdx(id)));
                }
                else
                {
                    // Reverse index also expired — best effort: remove from the searched key only
                    removeTasks.Add(removeBatch.SortedSetRemoveAsync(sourceKey, id));
                }
            }
            removeBatch.Execute();
            await Task.WhenAll(removeTasks);
        }
        catch
        {
            // fire-and-forget: never propagate
        }
    }

    private List<RedisKey> ResolveFilterKeys(JobSearchQuery query)
    {
        var keys = ResolveBaseFilterKeys(query);
        if (!string.IsNullOrEmpty(query.State))
            keys.Add((RedisKey)V2StateIdx(query.State!));
        return keys;
    }

    private List<RedisKey> ResolveBaseFilterKeys(JobSearchQuery query)
    {
        var keys = new List<RedisKey>();
        if (!string.IsNullOrEmpty(query.JobType))
            keys.Add((RedisKey)V2TypeIdx(query.JobType!));
        foreach (var f in query.Filters)
            if (!string.IsNullOrEmpty(f.Value))
                keys.Add((RedisKey)V2FieldIdx(f.Key, f.Value));
        return keys;
    }

    private static bool IsCreatedAtSort(JobSearchQuery query)
    {
        if (query.Sorts.Count > 1) return false;
        if (query.Sorts.Count == 1)
            return string.Equals(query.Sorts[0].Field, "createdAt", StringComparison.OrdinalIgnoreCase);
        if (query.SortBy != null)
            return string.Equals(query.SortBy, "createdAt", StringComparison.OrdinalIgnoreCase);
        return true;
    }

    private static Order GetSortDirection(JobSearchQuery query)
    {
        if (query.Sorts.Count == 1)
            return query.Sorts[0].Descending ? Order.Descending : Order.Ascending;
        if (query.SortBy != null)
            return query.SortDescending ? Order.Descending : Order.Ascending;
        return Order.Descending;
    }

    private static JobSearchResultPage EmptyPage(int page, int pageSize) =>
        new JobSearchResultPage { Items = new List<JobSearchResult>(), TotalCount = 0, Page = page, PageSize = pageSize };

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

        string? Get(string key) { dict.TryGetValue(key, out var v); return v; }

        var createdAt = long.TryParse(Get("createdAt"), out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : DateTimeOffset.UtcNow;

        return new JobSearchMetadata
        {
            JobId = Get("jobId") ?? string.Empty,
            JobType = Get("jobType") ?? string.Empty,
            MethodName = Get("methodName") ?? string.Empty,
            State = Get("state") ?? string.Empty,
            CreatedAt = createdAt,
            Fields = fields,
        };
    }

    private static bool Matches(JobSearchMetadata meta, JobSearchQuery query)
    {
        if (query.State != null && !string.Equals(meta.State, query.State, StringComparison.OrdinalIgnoreCase))
            return false;
        if (query.JobType != null && !string.Equals(meta.JobType, query.JobType, StringComparison.OrdinalIgnoreCase))
            return false;
        foreach (var f in query.Filters)
        {
            if (!meta.Fields.TryGetValue(f.Key, out var v)) return false;
            if (!string.Equals(v, f.Value, StringComparison.OrdinalIgnoreCase)) return false;
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

            ordered = ordered is null
                ? (capturedSort.Descending ? items.OrderByDescending(key) : items.OrderBy(key))
                : (capturedSort.Descending ? ordered.ThenByDescending(key) : ordered.ThenBy(key));
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
