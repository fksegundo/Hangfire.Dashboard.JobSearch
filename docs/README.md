# Hangfire.Dashboard.JobSearch Documentation

Welcome to the technical documentation for **Hangfire.Dashboard.JobSearch**.

## Table of Contents

1. [Architecture & Internals](architecture.md)
   - Metadata extraction workflow
   - Client and state filter hooks
   - Storage provider contract (`IJobSearchStorage`, `IJobSearchBulkChildStorage`)
   - Async Dashboard dispatcher and custom UI rendering
2. [Redis Storage Provider](storage-redis.md)
   - V2 Server-Side Indexing model + Redis Cluster hash tags
   - Atomic state transitions & reverse-index cleanup via Lua scripts
   - Single-round-trip Lua `CountByStateAsync`
   - Date range (`ZRANGEBYSCORE`), multi-value (`ZUNIONSTORE`), and prefix (`ZRANGEBYLEX`) search
3. [SQL Server & PostgreSQL Storage Providers + Backfill](storage-sql.md)
   - `SqlServerJobSearchStorage` setup & schema
   - `PostgreSqlJobSearchStorage` setup & `text_pattern_ops` indexing
   - `JobSearchBackfillService` for indexing existing Hangfire jobs
4. [Parent/Child Job Joins](parent-child-joins.md)
   - Fan-out job correlation
   - Expandable sub-rows & per-state progress badges in Dashboard
   - Custom `ParentValueResolver`
5. [Configuration & Dashboard Usage](configuration-guide.md)
   - Fluent strongly-typed API, `[JobSearchField]` attributes, array JSONPath, and custom `ValueExtractor`
   - UI features (state tabs, partial auto-refresh, quick Requeue/Delete actions, CSV/JSON export, UI options)
   - Writing custom storage adapters
