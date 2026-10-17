# Configuration & Dashboard Guide

This guide covers options, field definitions, query capabilities, and UI features for **Hangfire.Dashboard.JobSearch**.

---

## 1. Field Configuration Reference

Each job method is registered with a `JobSearchDefinition` composed of one or more `JobSearchField` rules:

| Property | Type | Description |
|---|---|---|
| `Name` | `string` | Display label in table headers and the query param key for filtering. |
| `ArgumentIndex` | `int` | Zero-based index of the method argument in Hangfire job invocation. |
| `JsonPath` | `string` | Path inside serialized JSON argument (e.g. `$.Id`, `$.Customer.Email`). Optional for primitives. |
| `DisplayInList` | `bool` | Whether this field should be rendered as a column in the dashboard table. |
| `Filterable` | `bool` | Whether an input box should appear in the Advanced Filters bar for this field. |
| `Sortable` | `bool` | Whether clicking the table column header toggles ascending/descending order. |

```csharp
new JobSearchField
{
    Name = "OrderId",
    ArgumentIndex = 0,
    JsonPath = "$.OrderId",
    DisplayInList = true,
    Filterable = true,
    Sortable = true
}
```

---

## 2. Dashboard Interface Features

Access the dashboard at:
```text
https://your-domain/hangfire/job-search
```

### 2.1 State Tabs & Badges
- Filter jobs by current Hangfire state: `All`, `Enqueued`, `Processing`, `Succeeded`, `Failed`, `Deleted`, `Scheduled`, `Awaiting`.
- State counters are dynamically queried from `IJobSearchStorage.GetStateCountsAsync`.
- Badges match standard Hangfire color tokens.

### 2.2 Collapsible Advanced Filters
- Click `<details>` panel to expand field-specific inputs.
- Combine multiple filters: e.g. `State: Failed`, `TenantId: tenant-123`, `OrderId: ORD-9988`.

### 2.3 Multi-Column Sorting
- Click any sortable column header to sort ascending (`ASC`) or descending (`DESC`).
- Secondary sort order defaults to job creation timestamp (`CreatedAt`).

### 2.4 Live Auto-Refresh
- Built-in live auto-refresh toggle (every 5 seconds).
- The state (Paused vs Active) is remembered in browser `localStorage`.

---

## 3. Custom Storage Implementation

If you are using SQL Server, PostgreSQL, MongoDB, or another database for index storage, implement `IJobSearchStorage`:

```csharp
public class CustomJobSearchStorage : IJobSearchStorage
{
    public Task SaveMetadataAsync(string jobId, JobSearchMetadata metadata, CancellationToken cancellationToken = default)
    {
        // Persist metadata and indices
    }

    public Task UpdateStateAsync(string jobId, string state, CancellationToken cancellationToken = default)
    {
        // Update job state pointer
    }

    public Task<JobSearchResultPage> SearchAsync(JobSearchQuery query, CancellationToken cancellationToken = default)
    {
        // Apply filters, sorting, and pagination
    }

    public Task<IReadOnlyDictionary<string, int>> GetStateCountsAsync(string? jobType = null, CancellationToken cancellationToken = default)
    {
        // Return state counts
    }
}
```

Register your custom storage:
```csharp
builder.Services.AddHangfireJobSearch(options =>
{
    options.UseStorage<CustomJobSearchStorage>();
    // or options.UseStorage(sp => new CustomJobSearchStorage(...));
});
```
