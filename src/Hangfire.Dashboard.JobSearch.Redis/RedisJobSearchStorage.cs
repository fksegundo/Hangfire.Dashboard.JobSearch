using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace Hangfire.Dashboard.JobSearch;

public sealed class RedisJobSearchStorage : IJobSearchStorage, IJobSearchBulkChildStorage
{
    private readonly IConnectionMultiplexer _connection;
    private readonly string _prefix;
    private readonly TimeSpan? _expiry;
    private readonly int _maxSortWindow;

    private static readonly string[] KnownHangfireStates =
        new[] { "Enqueued", "Processing", "Succeeded", "Failed", "Deleted", "Scheduled", "Awaiting" };

    // KEYS[1]=metaHash  KEYS[2]=newStateIdx  KEYS[3]=revIdx
    // ARGV[1]=jobId  ARGV[2]=newState  ARGV[3]=stateIdxPrefix
    private static readonly string MoveStateScript =
        "if redis.call('EXISTS', KEYS[1]) == 0 then return 0 end\n" +
        "local old = redis.call('HGET', KEYS[1], 'state')\n" +
        "local score = tonumber(redis.call('HGET', KEYS[1], 'createdAt')) or 0\n" +
        "if old then\n" +
        "  local oldKey = ARGV[3] .. string.lower(tostring(old))\n" +
        "  redis.call('ZREM', oldKey, ARGV[1])\n" +
        "  redis.call('SREM', KEYS[3], oldKey)\n" +
        "end\n" +
        "redis.call('ZADD', KEYS[2], score, ARGV[1])\n" +
        "redis.call('SADD', KEYS[3], KEYS[2])\n" +
        "redis.call('HSET', KEYS[1], 'state', ARGV[2])\n" +
        "return 1";

    // KEYS[1]=tmpBaseKey, KEYS[2..baseCount+1]=baseFilterKeys, KEYS[baseCount+2..]=stateIdxKeys
    // ARGV[1]=baseCount, ARGV[2]=tmpStateKey, ARGV[3]=minScore, ARGV[4]=maxScore
    private static readonly string CountByStateScript =
        "local baseCount = tonumber(ARGV[1])\n" +
        "local tmpState = ARGV[2]\n" +
        "local minScore = ARGV[3]\n" +
        "local maxScore = ARGV[4]\n" +
        "local baseKey = KEYS[2]\n" +
        "local createdBase = false\n" +
        "if baseCount > 1 then\n" +
        "  local args = {}\n" +
        "  for i = 2, baseCount + 1 do args[#args + 1] = KEYS[i] end\n" +
        "  redis.call('ZINTERSTORE', KEYS[1], baseCount, unpack(args), 'AGGREGATE', 'MIN')\n" +
        "  baseKey = KEYS[1]\n" +
        "  createdBase = true\n" +
        "end\n" +
        "local counts = {}\n" +
        "for i = baseCount + 2, #KEYS do\n" +
        "  redis.call('ZINTERSTORE', tmpState, 2, baseKey, KEYS[i], 'AGGREGATE', 'MIN')\n" +
        "  if minScore == '-inf' and maxScore == '+inf' then\n" +
        "    counts[#counts + 1] = redis.call('ZCARD', tmpState)\n" +
        "  else\n" +
        "    counts[#counts + 1] = redis.call('ZCOUNT', tmpState, minScore, maxScore)\n" +
        "  end\n" +
        "end\n" +
        "redis.call('DEL', tmpState)\n" +
        "if createdBase then redis.call('DEL', KEYS[1]) end\n" +
        "return counts";

    public RedisJobSearchStorage(
        IConnectionMultiplexer connection,
        string keyPrefix = "hangfire:jobsearch",
        TimeSpan? expiry = null,
        int maxSortWindow = 5000,
        bool useClusterHashTag = false)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        if (keyPrefix is null) throw new ArgumentNullException(nameof(keyPrefix));
        _prefix = useClusterHashTag && !keyPrefix.Contains('{') && !keyPrefix.Contains('}')
            ? $"{{{keyPrefix}}}"
            : keyPrefix;
        _expiry = expiry;
        _maxSortWindow = maxSortWindow > 0 ? maxSortWindow : 5000;
    }

    private IDatabase Db => _connection.GetDatabase();

    private string V2MetaKey(string jobId) => $"{_prefix}:v2:meta:{jobId}";
    private string V2GlobalIdx => $"{_prefix}:v2:idx";
    private string V2StateIdx(string state) => $"{_prefix}:v2:idx:state:{state.ToLowerInvariant()}";
    private string V2TypeIdx(string type) => $"{_prefix}:v2:idx:type:{type.ToLowerInvariant()}";
    private string V2MethodIdx(string method) => $"{_prefix}:v2:idx:method:{method.ToLowerInvariant()}";
    private string V2FieldIdx(string field, string value) => $"{_prefix}:v2:idx:f:{field.ToLowerInvariant()}:{value.ToLowerInvariant()}";
    private string V2FieldLexIdx(string field) => $"{_prefix}:v2:lex:f:{field.ToLowerInvariant()}";
    private static string LexMember(string value, string jobId) => $"{value.ToLowerInvariant()}\0{jobId}";
    private string V2RevIdx(string jobId) => $"{_prefix}:v2:rev:{jobId}";
    private string V2StateIdxPrefix => $"{_prefix}:v2:idx:state:";
    private string V2TmpKey() => $"{_prefix}:v2:tmp:{Guid.NewGuid():N}";

    public async Task SaveMetadataAsync(string jobId, JobSearchMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(jobId)) throw new ArgumentException("Job id is required.", nameof(jobId));
        if (metadata is null) throw new ArgumentNullException(nameof(metadata));
        cancellationToken.ThrowIfCancellationRequested();

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
        if (!string.IsNullOrEmpty(metadata.MethodName))
            indexKeys.Add((RedisValue)V2MethodIdx(metadata.MethodName));
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
        if (!string.IsNullOrEmpty(metadata.MethodName))
            tasks.Add(batch.SortedSetAddAsync(V2MethodIdx(metadata.MethodName), jobId, score));
        tasks.Add(batch.SetAddAsync(revKey, indexKeys.ToArray()));

        foreach (var field in metadata.Fields)
            if (!string.IsNullOrEmpty(field.Value))
            {
                tasks.Add(batch.SortedSetAddAsync(V2FieldIdx(field.Key, field.Value!), jobId, score));
                tasks.Add(batch.SortedSetAddAsync(V2FieldLexIdx(field.Key), LexMember(field.Value!, jobId), 0));
            }

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
        cancellationToken.ThrowIfCancellationRequested();

        var db = Db;
        var metaKey = V2MetaKey(jobId);
        var revKey = V2RevIdx(jobId);

        await db.ScriptEvaluateAsync(
            MoveStateScript,
            keys: new RedisKey[] { metaKey, V2StateIdx(state), revKey },
            values: new RedisValue[] { jobId, state, V2StateIdxPrefix });

        if (_expiry.HasValue)
        {
            var batch = db.CreateBatch();
            var t1 = batch.KeyExpireAsync(metaKey, _expiry);
            var t2 = batch.KeyExpireAsync(revKey, _expiry.Value.Add(TimeSpan.FromDays(1)));
            batch.Execute();
            await Task.WhenAll(t1, t2);
        }
    }

    public async Task DeleteMetadataAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(jobId)) throw new ArgumentException("Job id is required.", nameof(jobId));
        cancellationToken.ThrowIfCancellationRequested();

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
        if (!string.IsNullOrEmpty(meta.MethodName))
            tasks2.Add(batch2.SortedSetRemoveAsync(V2MethodIdx(meta.MethodName), jobId));

        foreach (var field in meta.Fields)
            if (!string.IsNullOrEmpty(field.Value))
            {
                tasks2.Add(batch2.SortedSetRemoveAsync(V2FieldIdx(field.Key, field.Value!), jobId));
                tasks2.Add(batch2.SortedSetRemoveAsync(V2FieldLexIdx(field.Key), LexMember(field.Value!, jobId)));
            }

        batch2.Execute();
        await Task.WhenAll(tasks2);
    }

    public async Task<JobSearchResultPage> SearchAsync(JobSearchQuery query, CancellationToken cancellationToken = default)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));
        cancellationToken.ThrowIfCancellationRequested();

        var db = Db;
        var page = Math.Max(1, query.Page);
        var pageSize = query.PageSize <= 0 ? 20 : query.PageSize;

        if (!string.IsNullOrEmpty(query.JobId))
            return await SearchByJobIdAsync(db, query, page, pageSize);

        var tempKeys = new List<RedisKey>();
        var filterKeys = await ResolveFilterKeysAsync(db, query, tempKeys);

        RedisKey workingKey;

        try
        {
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
                var tmpKey = (RedisKey)V2TmpKey();
                tempKeys.Add(tmpKey);
                await db.SortedSetCombineAndStoreAsync(
                    SetOperation.Intersect, tmpKey, filterKeys.ToArray(), aggregate: Aggregate.Min);
                await db.KeyExpireAsync(tmpKey, TimeSpan.FromSeconds(30));
                workingKey = tmpKey;
            }

            var hasDateRange = query.CreatedFrom.HasValue || query.CreatedTo.HasValue;
            var minScore = query.CreatedFrom.HasValue ? (double)query.CreatedFrom.Value.ToUnixTimeMilliseconds() : double.NegativeInfinity;
            var maxScore = query.CreatedTo.HasValue ? (double)query.CreatedTo.Value.ToUnixTimeMilliseconds() : double.PositiveInfinity;

            var total = hasDateRange
                ? (int)await db.SortedSetLengthAsync(workingKey, minScore, maxScore)
                : (int)await db.SortedSetLengthAsync(workingKey);

            if (total == 0)
                return EmptyPage(page, pageSize);

            if (IsCreatedAtSort(query))
            {
                var order = GetSortDirection(query);
                var start = (long)(page - 1) * pageSize;
                var stop = (long)page * pageSize - 1;
                var jobIds = hasDateRange
                    ? await db.SortedSetRangeByScoreAsync(workingKey, minScore, maxScore, Exclude.None, order, skip: start, take: pageSize)
                    : await db.SortedSetRangeByRankAsync(workingKey, start, stop, order);
                var items = await LoadPageItemsAsync(db, jobIds, workingKey);
                return new JobSearchResultPage { Items = items, TotalCount = total, Page = page, PageSize = pageSize };
            }
            else
            {
                var windowSize = Math.Min(total, _maxSortWindow);
                var windowIds = hasDateRange
                    ? await db.SortedSetRangeByScoreAsync(workingKey, minScore, maxScore, Exclude.None, Order.Descending, skip: 0, take: windowSize)
                    : await db.SortedSetRangeByRankAsync(workingKey, 0, windowSize - 1, Order.Descending);
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
            if (tempKeys.Count > 0)
                await db.KeyDeleteAsync(tempKeys.ToArray());
        }
    }

    public async Task<IReadOnlyDictionary<string, int>> CountByStateAsync(JobSearchQuery query, CancellationToken cancellationToken = default)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));
        cancellationToken.ThrowIfCancellationRequested();

        var db = Db;
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        if (!string.IsNullOrEmpty(query.JobId))
        {
            var hash = await db.HashGetAllAsync(V2MetaKey(query.JobId!));
            if (hash.Length == 0) return counts;
            var meta = Deserialize(hash);
            if (!Matches(meta, new JobSearchQuery
            {
                JobId = query.JobId,
                JobType = query.JobType,
                MethodName = query.MethodName,
                CreatedFrom = query.CreatedFrom,
                CreatedTo = query.CreatedTo,
                Filters = query.Filters
            }))
                return counts;
            counts[meta.State] = 1;
            return counts;
        }

        var tempKeys = new List<RedisKey>();
        var hasDateRange = query.CreatedFrom.HasValue || query.CreatedTo.HasValue;
        var minScore = query.CreatedFrom.HasValue ? (double)query.CreatedFrom.Value.ToUnixTimeMilliseconds() : double.NegativeInfinity;
        var maxScore = query.CreatedTo.HasValue ? (double)query.CreatedTo.Value.ToUnixTimeMilliseconds() : double.PositiveInfinity;
        var minScoreArg = query.CreatedFrom.HasValue ? query.CreatedFrom.Value.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture) : "-inf";
        var maxScoreArg = query.CreatedTo.HasValue ? query.CreatedTo.Value.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture) : "+inf";

        try
        {
            var baseKeys = await ResolveBaseFilterKeysAsync(db, query, tempKeys);

            if (baseKeys.Count == 0)
            {
                var batch = db.CreateBatch();
                var stateTasks = new List<(string State, Task<long> Task)>();
                foreach (var state in KnownHangfireStates)
                {
                    var task = hasDateRange
                        ? batch.SortedSetLengthAsync(V2StateIdx(state), minScore, maxScore)
                        : batch.SortedSetLengthAsync(V2StateIdx(state));
                    stateTasks.Add((state, task));
                }
                batch.Execute();

                foreach (var (state, task) in stateTasks)
                {
                    var count = (int)await task;
                    if (count > 0) counts[state] = count;
                }
            }
            else
            {
                var keys = new RedisKey[1 + baseKeys.Count + KnownHangfireStates.Length];
                keys[0] = (RedisKey)V2TmpKey();
                for (var i = 0; i < baseKeys.Count; i++)
                    keys[1 + i] = baseKeys[i];
                for (var i = 0; i < KnownHangfireStates.Length; i++)
                    keys[1 + baseKeys.Count + i] = (RedisKey)V2StateIdx(KnownHangfireStates[i]);

                var result = (RedisResult[]?)await db.ScriptEvaluateAsync(
                    CountByStateScript,
                    keys: keys,
                    values: new RedisValue[] { baseKeys.Count, V2TmpKey(), minScoreArg, maxScoreArg });

                if (result != null)
                {
                    for (var i = 0; i < KnownHangfireStates.Length && i < result.Length; i++)
                    {
                        var count = (int)result[i];
                        if (count > 0) counts[KnownHangfireStates[i]] = count;
                    }
                }
            }

            return counts;
        }
        finally
        {
            if (tempKeys.Count > 0)
                await db.KeyDeleteAsync(tempKeys.ToArray());
        }
    }

    public async Task<Dictionary<string, List<JobSearchResult>>> SearchChildrenBulkAsync(
        string childJobType,
        string childMethodName,
        string keyField,
        IReadOnlyList<string> parentKeys,
        int maxPerKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
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

    private async Task<List<RedisKey>> ResolveFilterKeysAsync(IDatabase db, JobSearchQuery query, List<RedisKey> tempKeys)
    {
        var keys = await ResolveBaseFilterKeysAsync(db, query, tempKeys);
        if (!string.IsNullOrEmpty(query.State))
            keys.Add((RedisKey)V2StateIdx(query.State!));
        return keys;
    }

    private async Task<List<RedisKey>> ResolveBaseFilterKeysAsync(IDatabase db, JobSearchQuery query, List<RedisKey> tempKeys)
    {
        var keys = new List<RedisKey>();
        if (!string.IsNullOrEmpty(query.JobType))
            keys.Add((RedisKey)V2TypeIdx(query.JobType!));
        if (!string.IsNullOrEmpty(query.MethodName))
            keys.Add((RedisKey)V2MethodIdx(query.MethodName!));

        foreach (var f in query.Filters)
        {
            var terms = JobSearchFilterMatcher.ParseTerms(f.Value);
            if (terms.Count == 0) continue;

            if (terms.Count == 1)
            {
                keys.Add(await ResolveSingleFieldTermKeyAsync(db, f.Key, terms[0], tempKeys));
            }
            else
            {
                var termKeys = new List<RedisKey>(terms.Count);
                foreach (var term in terms)
                    termKeys.Add(await ResolveSingleFieldTermKeyAsync(db, f.Key, term, tempKeys));

                var unionKey = (RedisKey)V2TmpKey();
                tempKeys.Add(unionKey);
                await db.SortedSetCombineAndStoreAsync(SetOperation.Union, unionKey, termKeys.ToArray(), aggregate: Aggregate.Min);
                await db.KeyExpireAsync(unionKey, TimeSpan.FromSeconds(30));
                keys.Add(unionKey);
            }
        }

        return keys;
    }

    private async Task<RedisKey> ResolveSingleFieldTermKeyAsync(IDatabase db, string field, string term, List<RedisKey> tempKeys)
    {
        if (!JobSearchFilterMatcher.IsPrefixTerm(term, out var prefix))
        {
            return (RedisKey)V2FieldIdx(field, term);
        }

        var lowerPrefix = prefix.ToLowerInvariant();
        var lexMatches = await db.SortedSetRangeByValueAsync(
            V2FieldLexIdx(field),
            min: (RedisValue)lowerPrefix,
            max: (RedisValue)(lowerPrefix + "\uffff"),
            exclude: Exclude.None,
            order: Order.Ascending,
            skip: 0,
            take: _maxSortWindow);

        var tmpKey = (RedisKey)V2TmpKey();
        tempKeys.Add(tmpKey);

        if (lexMatches.Length == 0)
        {
            return tmpKey;
        }

        var jobIds = new List<string>(lexMatches.Length);
        foreach (var entry in lexMatches)
        {
            var raw = (string?)entry;
            if (raw is null) continue;
            var sep = raw.IndexOf('\0');
            if (sep >= 0 && sep + 1 < raw.Length)
                jobIds.Add(raw.Substring(sep + 1));
        }

        if (jobIds.Count == 0)
        {
            return tmpKey;
        }

        var scoreBatch = db.CreateBatch();
        var scoreTasks = jobIds.Select(id => (id, scoreBatch.SortedSetScoreAsync(V2GlobalIdx, id))).ToList();
        scoreBatch.Execute();

        var entries = new List<SortedSetEntry>(jobIds.Count);
        foreach (var (id, task) in scoreTasks)
        {
            var score = await task;
            if (score.HasValue)
                entries.Add(new SortedSetEntry(id, score.Value));
        }

        if (entries.Count > 0)
        {
            await db.SortedSetAddAsync(tmpKey, entries.ToArray());
            await db.KeyExpireAsync(tmpKey, TimeSpan.FromSeconds(30));
        }

        return tmpKey;
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
        if (query.MethodName != null && !string.Equals(meta.MethodName, query.MethodName, StringComparison.OrdinalIgnoreCase))
            return false;
        if (query.CreatedFrom.HasValue && meta.CreatedAt < query.CreatedFrom.Value)
            return false;
        if (query.CreatedTo.HasValue && meta.CreatedAt > query.CreatedTo.Value)
            return false;
        foreach (var f in query.Filters)
        {
            if (string.IsNullOrWhiteSpace(f.Value)) continue;
            if (!meta.Fields.TryGetValue(f.Key, out var v)) return false;
            if (!JobSearchFilterMatcher.Matches(v, f.Value)) return false;
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
            Func<JobSearchMetadata, JobSearchSortValue> key = capturedSort.Field.ToLowerInvariant() switch
            {
                "jobid" => m => JobSearchSortValue.From(m.JobId),
                "state" => m => JobSearchSortValue.From(m.State),
                "jobtype" => m => JobSearchSortValue.From(m.JobType),
                "methodname" => m => JobSearchSortValue.From(m.MethodName),
                "createdat" => m => JobSearchSortValue.From(m.CreatedAt.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture)),
                _ => m => JobSearchSortValue.From(m.Fields.TryGetValue(capturedSort.Field, out var v) ? v : null),
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
