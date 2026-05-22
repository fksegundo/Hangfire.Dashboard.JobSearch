# Hangfire.Dashboard.JobSearch

Hangfire Dashboard extension for indexing job arguments and showing them as searchable, sortable columns inside the Hangfire Dashboard.

The project is intended for applications where the default Hangfire job lists are not enough to find a job by business-specific values stored in job arguments, such as tenant IDs, document IDs, customer IDs, order IDs, or any other field your application passes to background jobs.

## Current Status

This repository is in early development. The current implementation provides:

- job definitions configured in code;
- metadata extraction from job arguments using `ArgumentIndex` and `JsonPath`;
- client and state filters that keep the search index updated;
- an integrated Hangfire Dashboard page at `/hangfire/job-search`;
- configurable display columns, filters, and sortable fields;
- state counts, pagination, job details links, and multi-column sorting;
- an in-memory storage implementation;
- a storage abstraction for custom implementations.

Persistent storage adapters for Hangfire storages such as SQL Server or Redis are not implemented yet.

## Repository Layout

```text
src/Hangfire.Dashboard.JobSearch/          Library source
tests/Hangfire.Dashboard.JobSearch.Tests/  Unit tests
examples/Hangfire.Dashboard.JobSearch.Example/  Runnable sample app
docs/                                      Specs and design notes
```

## Quick Start

Register Hangfire and JobSearch in your application services:

```csharp
builder.Services.AddHangfire(configuration => configuration.UseMemoryStorage());
builder.Services.AddHangfireServer();

builder.Services.AddHangfireJobSearch(options =>
{
    options.AddJobDefinition(new JobSearchDefinition
    {
        JobType = typeof(WorkerA),
        MethodName = nameof(WorkerA.RunJobAsync),
        Fields =
        {
            new JobSearchField
            {
                Name = "Field1",
                ArgumentIndex = 0,
                JsonPath = "$.Field1",
                DisplayInList = true,
                Filterable = true,
                Sortable = true
            },
            new JobSearchField
            {
                Name = "Field2",
                ArgumentIndex = 0,
                JsonPath = "$.Field2",
                DisplayInList = true,
                Filterable = true,
                Sortable = true
            }
        }
    });
});
```

Register the Hangfire dashboard and JobSearch filters/page:

```csharp
app.UseHangfireDashboard("/hangfire");
GlobalConfiguration.Configuration.UseJobSearch(app.Services);
```

Open the dashboard page:

```text
/hangfire/job-search
```

## Field Configuration

Each `JobSearchDefinition` maps one job type and method to a set of fields:

- `JobType`: worker type that owns the job method.
- `MethodName`: method name to match.
- `Name`: field name shown in the dashboard and used in filters.
- `ArgumentIndex`: zero-based job argument index.
- `JsonPath`: path inside the serialized argument object.
- `DisplayInList`: show this field as a table column.
- `Filterable`: show this field as a filter input.
- `Sortable`: allow this field to be used in sorting.

The extractor currently supports the configured argument index and simple JSON paths through the serialized job argument.

## Storage

By default, JobSearch uses `InMemoryJobSearchStorage`. This is useful for local validation, but metadata is lost when the process restarts.

You can provide a custom storage implementation:

```csharp
builder.Services.AddHangfireJobSearch(options =>
{
    options.AddJobDefinition(...);
    options.UseStorage<MyJobSearchStorage>();
});
```

Or with a factory:

```csharp
builder.Services.AddHangfireJobSearch(options =>
{
    options.AddJobDefinition(...);
    options.UseStorage(provider => new MyJobSearchStorage(...));
});
```

Custom storage implementations must implement `IJobSearchStorage`.

## Running the Example

Start the sample app:

```bash
dotnet run --project examples/Hangfire.Dashboard.JobSearch.Example/Hangfire.Dashboard.JobSearch.Example.csproj --urls http://127.0.0.1:5099
```

Open:

```text
http://127.0.0.1:5099/hangfire/job-search
```

Enqueue sample jobs:

```bash
python3 examples/Hangfire.Dashboard.JobSearch.Example/scripts/enqueue_jobs.py --base-url http://127.0.0.1:5099 --count 25
```

The example jobs delay for 30 seconds so processing jobs can be seen in the dashboard.

## Development

Build and test:

```bash
dotnet test Hangfire.Dashboard.JobSearch.slnx --no-restore
```

Restore first when dependencies are not available locally:

```bash
dotnet restore Hangfire.Dashboard.JobSearch.slnx
dotnet test Hangfire.Dashboard.JobSearch.slnx --no-restore
```

## Roadmap

Planned topics:

- persistent storage adapters compatible with common Hangfire storages;
- stronger typed configuration helpers;
- broader dashboard actions, if they can be integrated without forking Hangfire;
- packaging metadata for NuGet publication.

## License

This repository is licensed under [Apache-2.0](LICENSE).

## Third-Party Licenses

This project depends on third-party packages. Those dependencies keep their own license terms, and usage/distribution must comply with each dependency license.

For Hangfire packages, see the official licensing page:

- https://www.hangfire.io/licenses.html
