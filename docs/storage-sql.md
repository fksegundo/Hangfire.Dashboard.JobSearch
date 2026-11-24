# SQL Server & PostgreSQL Storage Providers

In addition to Redis and In-Memory storage, **Hangfire.Dashboard.JobSearch** provides dedicated relational storage packages for **SQL Server** and **PostgreSQL**, plus a **Backfill service** for indexing pre-existing Hangfire jobs.

---

## 1. SQL Server (`fksegundo.Hangfire.Dashboard.JobSearch.SqlServer`)

Install the lightweight core package + SQL Server provider:

```bash
dotnet add package fksegundo.Hangfire.Dashboard.JobSearch.Core
dotnet add package fksegundo.Hangfire.Dashboard.JobSearch.SqlServer
```

### Registration

```csharp
using Hangfire.Dashboard.JobSearch;

builder.Services.AddHangfireJobSearch(options =>
{
    options.UseSqlServerStorage(
        connectionString: builder.Configuration.GetConnectionString("Hangfire")!,
        storageOptions: new SqlServerJobSearchOptions
        {
            SchemaName = "HangFire",
            TablePrefix = "JobSearch",
            PrepareSchemaIfNecessary = true,
            CommandTimeoutSeconds = 30
        });

    // Register jobs...
});
```

### Schema & Indexes

When `PrepareSchemaIfNecessary` is `true`, the provider automatically creates:
- `[HangFire].[JobSearch_Jobs]` (`JobId` PK, `JobType`, `MethodName`, `State`, `CreatedAtUtc`) with nonclustered indexes on `(State, CreatedAtUtc DESC)`, `(JobType, MethodName)`, and `(CreatedAtUtc DESC)`.
- `[HangFire].[JobSearch_JobFields]` (`JobId`, `FieldName`, `FieldValue`, FK with `ON DELETE CASCADE`) with index `(FieldName, FieldValue)`.

---

## 2. PostgreSQL (`fksegundo.Hangfire.Dashboard.JobSearch.PostgreSql`)

Install the lightweight core package + PostgreSQL provider:

```bash
dotnet add package fksegundo.Hangfire.Dashboard.JobSearch.Core
dotnet add package fksegundo.Hangfire.Dashboard.JobSearch.PostgreSql
```

### Registration

```csharp
using Hangfire.Dashboard.JobSearch;

builder.Services.AddHangfireJobSearch(options =>
{
    options.UsePostgreSqlStorage(
        connectionString: builder.Configuration.GetConnectionString("Hangfire")!,
        storageOptions: new PostgreSqlJobSearchOptions
        {
            SchemaName = "hangfire",
            TablePrefix = "job_search",
            PrepareSchemaIfNecessary = true,
            CommandTimeoutSeconds = 30
        });

    // Register jobs...
});
```

### Schema & Indexes

When `PrepareSchemaIfNecessary` is `true`, the provider automatically creates:
- `"hangfire"."job_search_jobs"` with B-Tree indexes on `(LOWER(state), created_at_utc DESC)`, `(LOWER(job_type), LOWER(method_name))`, and `(created_at_utc DESC)`.
- `"hangfire"."job_search_job_fields"` with a `text_pattern_ops` index on `(field_name, LOWER(field_value) text_pattern_ops)` for fast case-insensitive prefix (`prefix*`) searches and `= ANY(@keys)` bulk child lookups.

---

## 3. Backfilling Pre-Existing Hangfire Jobs (`JobSearchBackfillService`)

When adopting JobSearch in an existing production application, use `JobSearchBackfillService` to scan and index jobs already stored in Hangfire:

```csharp
var backfill = new JobSearchBackfillService(
    serviceProvider.GetRequiredService<JobSearchOptions>(),
    serviceProvider.GetRequiredService<IJobMetadataExtractor>(),
    serviceProvider.GetRequiredService<IJobSearchStorage>(),
    serviceProvider.GetService<ILogger<JobSearchBackfillService>>());

var summary = await backfill.BackfillAsync(
    JobStorage.Current,
    maxPerState: 1000,
    batchSize: 100);

Console.WriteLine($"Scanned: {summary.Scanned}, Indexed: {summary.Indexed}, Skipped: {summary.Skipped}, Errors: {summary.Errors}");
```
