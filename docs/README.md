# Hangfire.Dashboard.JobSearch Documentation

Welcome to the technical documentation for **Hangfire.Dashboard.JobSearch**.

## Table of Contents

1. [Architecture & Internals](architecture.md)
   - Metadata extraction workflow
   - Client and state filter hooks
   - Storage provider contract
   - Dashboard dispatcher and custom UI rendering
2. [Redis Storage Provider (Production Ready)](storage-redis.md)
   - V2 Server-Side Indexing model
   - Atomic state transitions via Lua scripts
   - O(PageSize) pagination via `ZINTERSTORE`
   - Reverse index cleanup and TTL support
3. [Parent/Child Job Joins](parent-child-joins.md)
   - Fan-out job correlation
   - Expandable sub-rows in dashboard
   - Custom `ParentValueResolver`
4. [Configuration & Dashboard Usage](configuration-guide.md)
   - Field options (`ArgumentIndex`, `JsonPath`, sorting, filtering)
   - UI features (collapsible filters, live refresh, state tabs)
   - Writing custom storage adapters
