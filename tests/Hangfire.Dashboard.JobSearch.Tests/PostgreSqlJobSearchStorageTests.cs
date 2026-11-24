using Testcontainers.PostgreSql;

namespace Hangfire.Dashboard.JobSearch.Tests;

[Trait("Category", "PostgreSql")]
public sealed class PostgreSqlJobSearchStorageTests : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;
    private PostgreSqlJobSearchStorage _storage = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .Build();
        await _container.StartAsync();
        _storage = new PostgreSqlJobSearchStorage(_container.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        await _container.StopAsync();
        await _container.DisposeAsync();
    }

    private static JobSearchMetadata Meta(
        string jobId,
        string jobType = "OrderWorker",
        string methodName = "ProcessAsync",
        string state = "Enqueued",
        DateTimeOffset? createdAt = null,
        params (string Key, string Value)[] fields) =>
        new JobSearchMetadata
        {
            JobId = jobId,
            JobType = jobType,
            MethodName = methodName,
            State = state,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            Fields = fields.ToDictionary(p => p.Key, p => (string?)p.Value)
        };

    [Fact]
    public async Task SaveSearchFilterSortPaginateAndDelete_WorkEndToEnd()
    {
        var t0 = DateTimeOffset.UtcNow.AddHours(-3);
        await _storage.SaveMetadataAsync("pg-1", Meta("pg-1", state: "Succeeded", createdAt: t0, fields: new[] { ("TenantId", "acme"), ("OrderId", "ORD-100"), ("Amount", "100") }));
        await _storage.SaveMetadataAsync("pg-2", Meta("pg-2", state: "Failed", createdAt: t0.AddHours(1), fields: new[] { ("TenantId", "globex"), ("OrderId", "ORD-105"), ("Amount", "7") }));
        await _storage.SaveMetadataAsync("pg-3", Meta("pg-3", state: "Enqueued", createdAt: t0.AddHours(2), fields: new[] { ("TenantId", "acme"), ("OrderId", "INV-200"), ("Amount", "42") }));

        // Prefix + case-insensitive
        var prefix = await _storage.SearchAsync(new JobSearchQuery { Filters = { ["OrderId"] = "ord-10*" } });
        Assert.Equal(2, prefix.TotalCount);

        // Multi-value IN
        var multi = await _storage.SearchAsync(new JobSearchQuery { Filters = { ["TenantId"] = "acme,globex" } });
        Assert.Equal(3, multi.TotalCount);

        // Date range
        var ranged = await _storage.SearchAsync(new JobSearchQuery { CreatedFrom = t0.AddMinutes(30), CreatedTo = t0.AddMinutes(90) });
        Assert.Equal("pg-2", Assert.Single(ranged.Items).JobId);

        // Numeric field sort
        var sorted = await _storage.SearchAsync(new JobSearchQuery { SortBy = "Amount", SortDescending = false });
        Assert.Equal(new[] { "pg-2", "pg-3", "pg-1" }, sorted.Items.Select(x => x.JobId));

        // UpdateState + CountByState
        await _storage.UpdateStateAsync("pg-3", "Succeeded");
        var counts = await _storage.CountByStateAsync(new JobSearchQuery { Filters = { ["TenantId"] = "acme" } });
        Assert.Equal(2, counts["Succeeded"]);

        // Delete
        await _storage.DeleteMetadataAsync("pg-3");
        var afterDelete = await _storage.SearchAsync(new JobSearchQuery { JobId = "pg-3" });
        Assert.Empty(afterDelete.Items);
    }

    [Fact]
    public async Task SearchChildrenBulkAsync_ReturnsMappedChildren()
    {
        await _storage.SaveMetadataAsync("pg-c1", Meta("pg-c1", jobType: "InvoiceWorker", methodName: "GenerateAsync", state: "Succeeded", fields: ("OrderId", "ORD-99")));
        await _storage.SaveMetadataAsync("pg-c2", Meta("pg-c2", jobType: "InvoiceWorker", methodName: "GenerateAsync", state: "Failed", fields: ("OrderId", "ORD-99")));

        var bulk = await _storage.SearchChildrenBulkAsync("InvoiceWorker", "GenerateAsync", "OrderId", new[] { "ORD-99" }, 100);
        Assert.True(bulk.TryGetValue("ORD-99", out var children));
        Assert.Equal(2, children.Count);
    }
}
