# Configuration & Dashboard Guide

This guide covers options, field definitions, query capabilities, and UI features for **Hangfire.Dashboard.JobSearch**.

---

## 1. Field Configuration Reference

Each job method is registered with a `JobSearchDefinition` composed of one or more `JobSearchField` rules:

| Property | Type | Description |
|---|---|---|
| `Name` | `string` | Display label in table headers and the query param key for filtering. |
| `ArgumentIndex` | `int` | Zero-based index of the method argument in Hangfire job invocation. |
| `JsonPath` | `string` | Path inside job argument (e.g. `$.Id`, `$.Customer.Email`, `$.Items[0].Sku`). Optional when `ValueExtractor` is provided. |
| `ValueExtractor` | `Func<Job, string?>?` | Optional custom delegate to compute/extract the field value directly from the `Job`. |
| `DisplayInList` | `bool` | Whether this field should be rendered as a column in the dashboard table. |
| `Filterable` | `bool` | Whether an input box should appear in the Advanced Filters bar for this field. |
| `Sortable` | `bool` | Whether sort controls are rendered for this column header. |

### Fluent API & `[JobSearchField]` Attributes

```csharp
options.AddJob<OrderWorker>(w => w.ProcessOrderAsync(default!), job => job
    .Field<OrderJobArgs>(a => a.TenantId, sortable: true)
    .Field("FirstSku", 0, "$.Items[0].Sku", displayInList: true, filterable: true)
    .Field("Computed", j => $"args:{j.Args.Count}", displayInList: true, filterable: false)
    .DiscoverFieldsFromAttributes());
```

### UI Customization Options

```csharp
options.RoutePath = "/job-search";
options.MenuTitle = "Job Search";
options.AutoRefreshIntervalSeconds = 5; // set <= 0 to start paused
```

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
- Filter jobs by current Hangfire state via clickable pill tabs: `All`, `Processing`, `Enqueued`, `Succeeded`, `Failed`, `Scheduled`, `Awaiting`, `Deleted`.
- State counters are dynamically queried from `IJobSearchStorage.CountByStateAsync`.
- Failed jobs display an exception message snippet and Succeeded jobs display execution duration.

### 2.2 Collapsible Advanced Filters & Export
- Click the `Filters` panel to expand field-specific inputs and UTC date range inputs (`Created From` / `Created To`).
- Supports exact match (`acme`), multi-value `IN` (`acme,globex`), and prefix wildcards (`ORD-10*`).
- Export filtered results directly as **CSV** or **JSON** (`Export CSV` / `Export JSON`).

### 2.3 Multi-Column Sorting & Page Size
- Sort ascending/descending or add secondary sorts on any sortable column header.
- Choose `20`, `50`, or `100` jobs per page.

### 2.4 Partial Live Auto-Refresh & Quick Actions
- Built-in live auto-refresh updates only the results region via `fetch()`, preserving expanded parent/child rows, selections, and active input focus.
- Requeue or Delete jobs individually or in bulk directly from the table (automatically disabled when Hangfire Dashboard is `IsReadOnly`).

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

    public Task<IReadOnlyDictionary<string, int>> CountByStateAsync(JobSearchQuery query, CancellationToken cancellationToken = default)
    {
        // Return state counts for the query (ignoring State filter)
    }

    public Task DeleteMetadataAsync(string jobId, CancellationToken cancellationToken = default)
    {
        // Remove metadata and index entries
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
