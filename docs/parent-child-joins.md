# Parent/Child Job Joins (Fan-Out Correlation)

In distributed architectures and batch processing systems, parent jobs frequently fan out into multiple sub-jobs (e.g. partition workers, chunk importers, child tasks).

`Hangfire.Dashboard.JobSearch` provides native support for **Parent/Child Joins** (`JobSearchJoin`), allowing related child jobs to be nested and displayed directly beneath their parent row in the Hangfire Dashboard.

---

## 1. How Joins Work

When a `JobSearchJoin` is registered:
1. The dashboard detects when a rendered row matches the `ParentJobType` and `ParentMethodName`.
2. It extracts the shared correlation value using `KeyField` (or an optional `ParentValueResolver`).
3. It performs a fast lookup in the storage provider for all jobs matching `ChildJobType` and `ChildMethodName` where `KeyField` equals that value.
4. The dashboard renders an interactive expand/collapse toggle (`+ [N children]`) on the parent row. Clicking the button reveals the child jobs with their individual states, timestamps, and parameters.

---

## 2. Configuration Example

Suppose you have a report generator job that enqueues batch partition jobs:

```csharp
public class ReportWorker
{
    public Task GenerateReportAsync(ReportRequest request) { /* ... */ }
}

public class PartitionWorker
{
    public Task ProcessPartitionAsync(PartitionRequest request) { /* ... */ }
}
```

Both argument models contain a `ReportId`:
```csharp
public record ReportRequest(string ReportId, string Format);
public record PartitionRequest(string ReportId, int ChunkIndex);
```

### Registering the Definitions & Join

```csharp
builder.Services.AddHangfireJobSearch(options =>
{
    // 1. Parent Definition
    options.AddJobDefinition(new JobSearchDefinition
    {
        JobType = typeof(ReportWorker),
        MethodName = nameof(ReportWorker.GenerateReportAsync),
        Fields =
        {
            new JobSearchField { Name = "ReportId", ArgumentIndex = 0, JsonPath = "$.ReportId", DisplayInList = true, Filterable = true }
        }
    });

    // 2. Child Definition
    options.AddJobDefinition(new JobSearchDefinition
    {
        JobType = typeof(PartitionWorker),
        MethodName = nameof(PartitionWorker.ProcessPartitionAsync),
        Fields =
        {
            new JobSearchField { Name = "ReportId", ArgumentIndex = 0, JsonPath = "$.ReportId", DisplayInList = true },
            new JobSearchField { Name = "ChunkIndex", ArgumentIndex = 0, JsonPath = "$.ChunkIndex", DisplayInList = true }
        }
    });

    // 3. Join Declaration
    options.AddJoin(new JobSearchJoin
    {
        KeyField = "ReportId",
        ParentJobType = typeof(ReportWorker),
        ParentMethodName = nameof(ReportWorker.GenerateReportAsync),
        ChildJobType = typeof(PartitionWorker),
        ChildMethodName = nameof(PartitionWorker.ProcessPartitionAsync),
        ChildLabel = "chunks" // Displays: "[+] 8 chunks"
    });
});
```

---

## 3. Dynamic Value Resolution (`ParentValueResolver`)

If the parent job resolves or computes the correlation key dynamically (or defaults to its own Hangfire `JobId` when empty), you can provide a custom `ParentValueResolver`:

```csharp
options.AddJoin(new JobSearchJoin
{
    KeyField = "BatchId",
    ParentJobType = typeof(BatchWorker),
    ParentMethodName = nameof(BatchWorker.ProcessAsync),
    ChildJobType = typeof(SubItemWorker),
    ChildMethodName = nameof(SubItemWorker.ProcessItemAsync),
    ParentValueResolver = parentResult =>
    {
        // If BatchId was provided in arguments, use it; otherwise fallback to the Parent Job's ID
        if (parentResult.Fields.TryGetValue("BatchId", out var batchId) && !string.IsNullOrWhiteSpace(batchId))
        {
            return batchId;
        }

        return parentResult.JobId;
    }
});
```
