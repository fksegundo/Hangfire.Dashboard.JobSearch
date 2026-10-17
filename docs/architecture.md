# Architecture & Internals

This document describes the internal architecture of **Hangfire.Dashboard.JobSearch**, explaining how jobs are intercepted, indexed, queried, and rendered inside the Hangfire Dashboard.

---

## 1. High-Level Architecture Overview

```
                      +-----------------------------+
                      |   Hangfire Job Scheduling   |
                      |  (BackgroundJob.Enqueue...) |
                      +--------------+--------------+
                                     |
                                     v
                       +---------------------------+
                       |   JobSearchClientFilter   |
                       |  (IServerFilter / Client) |
                       +-------------+-------------+
                                     |  Extracts metadata via
                                     |  DefaultJobMetadataExtractor
                                     v
                        +-------------------------+
                        |    IJobSearchStorage    |
                        | (Redis / In-Memory /..) |
                        +------------+------------+
                                     ^
                                     |  Updates state upon
                        +------------+------------+  lifecycle changes
                        |    JobSearchStateFilter |
                        | (ElectState / ApplyState|
                        +-------------------------+
                                     |
                                     | Reads indices & metadata
                                     v
                       +---------------------------+
                       |    Hangfire Dashboard     |
                       |  JobSearchDashboardPage   |
                       |  JobSearchDashboardRender |
                       +---------------------------+
```

---

## 2. Key Components

### 2.1 Metadata Extraction (`DefaultJobMetadataExtractor`)
When a Hangfire job is created, `JobSearchClientFilter` intercepts the job before persistence:
1. Matches the executing worker `JobType` and `MethodName` against configured `JobSearchDefinition` entries.
2. Extracts argument data specified by `ArgumentIndex`.
3. If `JsonPath` is specified, uses a lightweight JSON path parser to extract nested scalar values (such as `$.TenantId` or `$.Order.Id`).
4. Produces a `JobSearchMetadata` record containing:
   - `JobId`
   - `JobType` & `MethodName`
   - `CreatedAt` timestamp
   - Extracted key-value `Fields`
   - Initial State (`Enqueued`, etc.)

### 2.2 Lifecycle State Tracking (`JobSearchStateFilter`)
Implements Hangfire's `IApplyStateFilter`:
- Triggered whenever a job transitions between states (e.g. `Enqueued` -> `Processing` -> `Succeeded` / `Failed` / `Deleted`).
- Calls `IJobSearchStorage.UpdateStateAsync(jobId, newState)` to keep dashboard filter tabs and counters accurate in real time.

### 2.3 Storage Abstraction (`IJobSearchStorage`)
Defines the contract needed for search, filtering, and pagination:
- `SaveMetadataAsync`: Persists initial metadata and indices for a newly enqueued job.
- `UpdateStateAsync`: Updates state and moves index pointers.
- `SearchAsync`: Queries jobs using criteria (`JobSearchQuery`), returning `JobSearchResultPage` with total count, metadata, and matched fields.
- `GetStateCountsAsync`: Returns job counts partitioned by state (`All`, `Enqueued`, `Processing`, `Succeeded`, `Failed`, `Deleted`).

### 2.4 Dashboard Dispatcher & Renderer
Integrated with Hangfire's custom page routing:
- **`JobSearchDashboardRazorDispatcher`**: Dispatches HTTP requests for `/hangfire/job-search`, parsing query params (`page`, `pageSize`, `jobType`, `state`, sort parameters, and field filters).
- **`JobSearchDashboardRenderer`**: Renders server-side HTML/CSS:
  - State badges (`Enqueued`, `Processing`, `Succeeded`, etc.) with standard Hangfire colors.
  - Collapsible advanced filter bar (`<details>`).
  - Searchable columns with multi-column sorting indicators.
  - Sub-row rendering for parent/child fan-out joins (`JobSearchJoin`).
  - Live auto-refresh toggle with client-side state saved in `localStorage`.
