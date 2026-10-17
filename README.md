# fksegundo.Hangfire.Dashboard.JobSearch

Hangfire Dashboard extension for indexing job arguments and showing them as searchable, sortable columns inside the Hangfire Dashboard.

The project is intended for applications where the default Hangfire job lists are not enough to find a job by business-specific values stored in job arguments, such as tenant IDs, document IDs, customer IDs, order IDs, or any other field your application passes to background jobs.

## Features

- **Business Parameter Search**: Index any job parameter by argument index and JSON path (e.g. `TenantId`, `OrderId`, `CustomerId`).
- **Production Redis Storage (`RedisJobSearchStorage`)**: Built on StackExchange.Redis with $O(\text{pageSize})$ server-side set intersections (`ZINTERSTORE`), atomic Lua state transitions, and automatic TTL index cleanup.
- **Parent/Child Fan-Out Joins (`JobSearchJoin`)**: Expand parent jobs into interactive sub-rows displaying associated child workers.
- **Integrated Hangfire Dashboard**: Available at `/hangfire/job-search` with state count tabs, colored badges, collapsible filter bars, multi-column sorting, and configurable live auto-refresh.
- **In-Memory Storage**: Built-in adapter for local testing and prototyping.
- **Extensible**: Clean `IJobSearchStorage` abstraction for building custom storages (SQL Server, PostgreSQL, MongoDB, etc.).

## Repository Layout

```text
src/Hangfire.Dashboard.JobSearch/               Core library (netstandard2.0)
tests/Hangfire.Dashboard.JobSearch.Tests/       Unit & Integration tests (xUnit + Testcontainers)
examples/Hangfire.Dashboard.JobSearch.Example/  Runnable sample app (.NET 10)
docs/                                           Detailed technical specifications & guides
```

## Quick Start

### 1. Register Services

```csharp
using Hangfire;
using Hangfire.Dashboard.JobSearch;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHangfire(config => config.UseMemoryStorage());
builder.Services.AddHangfireServer();

// Optional: Connect Redis for persistent search indexing
var redis = ConnectionMultiplexer.Connect("localhost:6379");

builder.Services.AddHangfireJobSearch(options =>
{
    // Use Redis storage (or InMemory storage by default)
    options.UseRedisStorage(redis, keyPrefix: "hangfire:jobsearch", expiry: TimeSpan.FromDays(7));

    // Configure jobs to index
    options.AddJobDefinition(new JobSearchDefinition
    {
        JobType = typeof(OrderWorker),
        MethodName = nameof(OrderWorker.ProcessOrderAsync),
        Fields =
        {
            new JobSearchField
            {
                Name = "TenantId",
                ArgumentIndex = 0,
                JsonPath = "$.TenantId",
                DisplayInList = true,
                Filterable = true,
                Sortable = true
            },
            new JobSearchField
            {
                Name = "OrderId",
                ArgumentIndex = 0,
                JsonPath = "$.OrderId",
                DisplayInList = true,
                Filterable = true,
                Sortable = true
            }
        }
    });

    // Optional: Configure parent-child correlation join
    options.AddJoin(new JobSearchJoin
    {
        KeyField = "OrderId",
        ParentJobType = typeof(OrderWorker),
        ParentMethodName = nameof(OrderWorker.ProcessOrderAsync),
        ChildJobType = typeof(InvoiceWorker),
        ChildMethodName = nameof(InvoiceWorker.GenerateInvoiceAsync),
        ChildLabel = "invoices"
    });
});
```

### 2. Configure Dashboard

```csharp
app.UseHangfireDashboard("/hangfire");
GlobalConfiguration.Configuration.UseJobSearch(app.Services);
```

### 3. Open the Dashboard

Navigate to:
```text
/hangfire/job-search
```

## Documentation

Comprehensive guides and architectural details are available in the [`/docs`](docs/) directory:

- [Architecture & Internals](docs/architecture.md): Lifecycle hooks, filters, metadata extraction pipeline.
- [Redis Storage Provider](docs/storage-redis.md): Server-side indexing data structures, Lua transitions, TTL cleanup.
- [Parent/Child Job Joins](docs/parent-child-joins.md): Fan-out correlation configuration and resolver.
- [Configuration & UI Guide](docs/configuration-guide.md): Field options, sorting, filters, and custom storages.

## Running the Example Application

Start the sample app:

```bash
dotnet run --project examples/Hangfire.Dashboard.JobSearch.Example/Hangfire.Dashboard.JobSearch.Example.csproj --urls http://127.0.0.1:5099
```

Open `http://127.0.0.1:5099/hangfire/job-search` and enqueue sample jobs using the Python script:

```bash
python3 examples/Hangfire.Dashboard.JobSearch.Example/scripts/enqueue_jobs.py --base-url http://127.0.0.1:5099 --count 25
```

## Running Tests

Execute test suite across supported runtimes:

```bash
dotnet test Hangfire.Dashboard.JobSearch.slnx
```

*(Integration tests automatically leverage Testcontainers for Redis).*

## Contributing

Contributions, bug reports, and pull requests are welcome! Please check out [CONTRIBUTING.md](CONTRIBUTING.md) and our [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).

## Security

Please report vulnerabilities responsibly according to our [SECURITY.md](SECURITY.md).

## License

This repository is licensed under the [Apache-2.0](LICENSE) License.

## Third-Party Licenses

This project depends on third-party packages. Those dependencies keep their own license terms:
- [Hangfire](https://www.hangfire.io/licenses.html)
- [StackExchange.Redis](https://github.com/StackExchange/StackExchange.Redis/blob/main/LICENSE)

