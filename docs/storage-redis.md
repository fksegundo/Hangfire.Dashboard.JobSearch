# Redis Storage Provider (`RedisJobSearchStorage`)

`Hangfire.Dashboard.JobSearch` includes an enterprise-grade, high-performance Redis storage adapter backed by `StackExchange.Redis`.

It implements a **V2 Server-Side Indexing Model** designed for high throughput, sub-millisecond pagination, full index cleanup on TTL expiry, and zero full-table memory scans.

---

## 1. Key Layout & Data Model

All Redis keys are partitioned using a configurable prefix (default: `hangfire:jobsearch`).

| Key Pattern | Type | Description |
|---|---|---|
| `{prefix}:v2:meta:{jobId}` | `Hash` | Full job metadata payload (`jobType`, `methodName`, `createdAt`, `state`, plus dynamic extracted field values). |
| `{prefix}:v2:idx` | `SortedSet` | Global index containing all active jobs. Score = `createdAt` timestamp (epoch milliseconds). |
| `{prefix}:v2:idx:state:{state}` | `SortedSet` | State index partitioned by lowercased state name (e.g. `enqueued`, `succeeded`). |
| `{prefix}:v2:idx:type:{type}` | `SortedSet` | Type index partitioned by worker type. |
| `{prefix}:v2:idx:f:{field}:{value}` | `SortedSet` | Secondary field index for exact-match filtering. |
| `{prefix}:v2:rev:{jobId}` | `Set` | Reverse lookup index containing all Redis keys referencing this `jobId`, guaranteeing clean teardown. |

---

## 2. Server-Side Index Operations

### Atomic State Transitions
State transitions occur atomically via an embedded Lua script (`MoveStateScript`):
```lua
if redis.call('EXISTS', KEYS[1]) == 0 then return 0 end
local old = redis.call('HGET', KEYS[1], 'state')
local score = tonumber(redis.call('HGET', KEYS[1], 'createdAt')) or 0
if old then redis.call('ZREM', ARGV[3] .. string.lower(tostring(old)), ARGV[1]) end
redis.call('ZADD', KEYS[2], score, ARGV[1])
redis.call('HSET', KEYS[1], 'state', ARGV[2])
return 1
```
This guarantees that moving a job between states (e.g., `Enqueued` -> `Processing`) produces no race conditions, stale pointers, or missing jobs.

### Intersected Queries (O(PageSize))
When filters are combined (e.g., `state=Processing` AND `type=WorkerA` AND `Field1=Value`):
1. A temporary key (`{prefix}:v2:tmp:{uuid}`) is computed using `ZINTERSTORE`.
2. Total count is fetched via `ZCARD` in $O(1)$.
3. The page slice is fetched using `ZRANGEBYSCORE` or `ZREVRANGEBYSCORE` limited to `[skip, skip + take]` in $O(\log N + M)$ where $M$ is the page size.
4. Metadata hashes are loaded in a pipelined batch.
5. The temporary key expires automatically or is removed immediately.

### Full Cleanup & Reverse Index
When TTL/expiry is configured:
- Every key created for a job (including field indices and state indices) is recorded in `{prefix}:v2:rev:{jobId}`.
- Orphaned indices are cleaned up automatically, avoiding memory leaks on high-volume Hangfire clusters.

---

## 3. Configuration & Usage

### Basic Registration
```csharp
using Hangfire.Dashboard.JobSearch;
using StackExchange.Redis;

var redis = ConnectionMultiplexer.Connect("localhost:6379");

builder.Services.AddHangfireJobSearch(options =>
{
    // Pass connection directly
    options.UseRedisStorage(
        connection: redis,
        keyPrefix: "my-app:jobsearch",
        expiry: TimeSpan.FromDays(7), // Auto-expire job indices after 7 days
        maxSortWindow: 5000           // Upper bound for in-memory secondary field sorting
    );

    // Register job definitions...
});
```

### Dependency Injection (Factory overload)
```csharp
builder.Services.AddHangfireJobSearch(options =>
{
    options.UseRedisStorage(
        connectionFactory: sp => sp.GetRequiredService<IConnectionMultiplexer>(),
        keyPrefix: "hangfire:jobsearch",
        expiry: TimeSpan.FromDays(14)
    );
});
```
