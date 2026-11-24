using Hangfire.MemoryStorage;
using Microsoft.Extensions.DependencyInjection;

namespace Hangfire.Dashboard.JobSearch.Tests;

public sealed class ModularPackagesAndBackfillTests
{
    [Fact]
    public void SqlServerStorageExtensions_RegistersSqlServerStorageAndValidatesOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHangfireJobSearch(options =>
        {
            options.UseSqlServerStorage("Server=localhost;Database=Hangfire;Trusted_Connection=True;");
        });

        var provider = services.BuildServiceProvider();
        var storage = provider.GetRequiredService<IJobSearchStorage>();
        Assert.IsType<SqlServerJobSearchStorage>(storage);
        Assert.IsAssignableFrom<IJobSearchBulkChildStorage>(storage);

        Assert.Throws<ArgumentException>(() =>
            new SqlServerJobSearchStorage("Server=localhost;", new SqlServerJobSearchOptions { SchemaName = "dbo; DROP TABLE Users" }));
    }

    [Fact]
    public void PostgreSqlStorageExtensions_RegistersPostgreSqlStorageAndValidatesOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHangfireJobSearch(options =>
        {
            options.UsePostgreSqlStorage("Host=localhost;Database=hangfire;Username=postgres;Password=postgres");
        });

        var provider = services.BuildServiceProvider();
        var storage = provider.GetRequiredService<IJobSearchStorage>();
        Assert.IsType<PostgreSqlJobSearchStorage>(storage);
        Assert.IsAssignableFrom<IJobSearchBulkChildStorage>(storage);

        Assert.Throws<ArgumentException>(() =>
            new PostgreSqlJobSearchStorage("Host=localhost;", new PostgreSqlJobSearchOptions { TablePrefix = "bad-prefix!" }));
    }

    [Fact]
    public async Task BackfillService_IndexesExistingHangfireJobs()
    {
        var hangfireStorage = new Hangfire.MemoryStorage.MemoryStorage();
        var client = new BackgroundJobClient(hangfireStorage);
        var jobId1 = client.Enqueue<BackfillWorker>(w => w.HandleAsync(new BackfillPayload { TenantId = "acme", OrderId = "ORD-1" }));
        var jobId2 = client.Schedule<BackfillWorker>(w => w.HandleAsync(new BackfillPayload { TenantId = "globex", OrderId = "ORD-2" }), TimeSpan.FromHours(1));

        var options = new JobSearchOptions();
        options.AddJob<BackfillWorker>(w => w.HandleAsync(default!), job => job
            .Field<BackfillPayload>(p => p.TenantId)
            .Field<BackfillPayload>(p => p.OrderId));

        var searchStorage = new InMemoryJobSearchStorage();
        var extractor = new DefaultJobMetadataExtractor(options);
        var backfill = new JobSearchBackfillService(options, extractor, searchStorage);

        var summary = await backfill.BackfillAsync(hangfireStorage);

        Assert.Equal(2, summary.Indexed);
        Assert.Equal(0, summary.Errors);

        var enqueued = await searchStorage.SearchAsync(new JobSearchQuery { State = "Enqueued", Filters = { ["TenantId"] = "acme" } });
        Assert.Equal(jobId1, Assert.Single(enqueued.Items).JobId);

        var scheduled = await searchStorage.SearchAsync(new JobSearchQuery { State = "Scheduled", Filters = { ["TenantId"] = "globex" } });
        Assert.Equal(jobId2, Assert.Single(scheduled.Items).JobId);
    }

    public sealed class BackfillWorker
    {
        public Task HandleAsync(BackfillPayload payload) => Task.CompletedTask;
    }

    public sealed class BackfillPayload
    {
        public string TenantId { get; set; } = string.Empty;
        public string OrderId { get; set; } = string.Empty;
    }
}
