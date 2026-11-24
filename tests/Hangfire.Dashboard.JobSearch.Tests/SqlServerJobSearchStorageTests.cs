using Testcontainers.MsSql;

namespace Hangfire.Dashboard.JobSearch.Tests;

[Trait("Category", "SqlServer")]
public sealed class SqlServerJobSearchStorageTests : IAsyncLifetime
{
    private MsSqlContainer _container = null!;
    private SqlServerJobSearchStorage _storage = null!;

    public async Task InitializeAsync()
    {
        _container = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();
        await _container.StartAsync();
        _storage = new SqlServerJobSearchStorage(_container.GetConnectionString());
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
        await _storage.SaveMetadataAsync("sql-1", Meta("sql-1", state: "Succeeded", createdAt: t0, fields: new[] { ("TenantId", "acme"), ("OrderId", "ORD-100"), ("Amount", "100") }));
        await _storage.SaveMetadataAsync("sql-2", Meta("sql-2", state: "Failed", createdAt: t0.AddHours(1), fields: new[] { ("TenantId", "globex"), ("OrderId", "ORD-105"), ("Amount", "7") }));
        await _storage.SaveMetadataAsync("sql-3", Meta("sql-3", state: "Enqueued", createdAt: t0.AddHours(2), fields: new[] { ("TenantId", "acme"), ("OrderId", "INV-200"), ("Amount", "42") }));

        // Prefix + case-insensitive
        var prefix = await _storage.SearchAsync(new JobSearchQuery { Filters = { ["OrderId"] = "ord-10*" } });
        Assert.Equal(2, prefix.TotalCount);

        // Multi-value IN
        var multi = await _storage.SearchAsync(new JobSearchQuery { Filters = { ["TenantId"] = "acme,globex" } });
        Assert.Equal(3, multi.TotalCount);

        // Date range
        var ranged = await _storage.SearchAsync(new JobSearchQuery { CreatedFrom = t0.AddMinutes(30), CreatedTo = t0.AddMinutes(90) });
        Assert.Equal("sql-2", Assert.Single(ranged.Items).JobId);

        // Numeric field sort
        var sorted = await _storage.SearchAsync(new JobSearchQuery { SortBy = "Amount", SortDescending = false });
        Assert.Equal(new[] { "sql-2", "sql-3", "sql-1" }, sorted.Items.Select(x => x.JobId));

        // UpdateState + CountByState
        await _storage.UpdateStateAsync("sql-3", "Succeeded");
        var counts = await _storage.CountByStateAsync(new JobSearchQuery { Filters = { ["TenantId"] = "acme" } });
        Assert.Equal(2, counts["Succeeded"]);

        // Delete
        await _storage.DeleteMetadataAsync("sql-3");
        var afterDelete = await _storage.SearchAsync(new JobSearchQuery { JobId = "sql-3" });
        Assert.Empty(afterDelete.Items);
    }

    [Fact]
    public async Task SearchChildrenBulkAsync_ReturnsMappedChildren()
    {
        await _storage.SaveMetadataAsync("sql-c1", Meta("sql-c1", jobType: "InvoiceWorker", methodName: "GenerateAsync", state: "Succeeded", fields: ("OrderId", "ORD-99")));
        await _storage.SaveMetadataAsync("sql-c2", Meta("sql-c2", jobType: "InvoiceWorker", methodName: "GenerateAsync", state: "Failed", fields: ("OrderId", "ORD-99")));

        var bulk = await _storage.SearchChildrenBulkAsync("InvoiceWorker", "GenerateAsync", "OrderId", new[] { "ORD-99" }, 100);
        Assert.True(bulk.TryGetValue("ORD-99", out var children));
        Assert.Equal(2, children.Count);
    }
}
