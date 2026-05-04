namespace Hangfire.Dashboard.JobSearch.Tests;

public sealed class InMemoryJobSearchStorageTests
{
    [Fact]
    public async Task SearchAsync_ReturnsResultsByOneField()
    {
        var storage = new InMemoryJobSearchStorage();
        await storage.SaveMetadataAsync("1", CreateMetadata("1", documento: "123", clienteId: "42"));
        await storage.SaveMetadataAsync("2", CreateMetadata("2", documento: "456", clienteId: "42"));

        var page = await storage.SearchAsync(new JobSearchQuery
        {
            Filters = { ["Documento"] = "123" }
        });

        var result = Assert.Single(page.Items);
        Assert.Equal("1", result.JobId);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_ReturnsResultsByTwoFields()
    {
        var storage = new InMemoryJobSearchStorage();
        await storage.SaveMetadataAsync("1", CreateMetadata("1", documento: "123", clienteId: "42"));
        await storage.SaveMetadataAsync("2", CreateMetadata("2", documento: "123", clienteId: "99"));
        await storage.SaveMetadataAsync("3", CreateMetadata("3", documento: "456", clienteId: "42"));

        var page = await storage.SearchAsync(new JobSearchQuery
        {
            Filters =
            {
                ["Documento"] = "123",
                ["ClienteId"] = "42"
            }
        });

        var result = Assert.Single(page.Items);
        Assert.Equal("1", result.JobId);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_FiltersByState()
    {
        var storage = new InMemoryJobSearchStorage();
        await storage.SaveMetadataAsync("1", CreateMetadata("1", state: "Succeeded"));
        await storage.SaveMetadataAsync("2", CreateMetadata("2", state: "Failed"));

        var page = await storage.SearchAsync(new JobSearchQuery
        {
            State = "Failed"
        });

        var result = Assert.Single(page.Items);
        Assert.Equal("2", result.JobId);
    }

    [Fact]
    public async Task SearchAsync_PaginatesResults()
    {
        var storage = new InMemoryJobSearchStorage();
        await storage.SaveMetadataAsync("1", CreateMetadata("1", createdAt: DateTimeOffset.Parse("2026-05-03T10:00:00Z")));
        await storage.SaveMetadataAsync("2", CreateMetadata("2", createdAt: DateTimeOffset.Parse("2026-05-03T11:00:00Z")));
        await storage.SaveMetadataAsync("3", CreateMetadata("3", createdAt: DateTimeOffset.Parse("2026-05-03T12:00:00Z")));

        var page = await storage.SearchAsync(new JobSearchQuery
        {
            Page = 2,
            PageSize = 1
        });

        var result = Assert.Single(page.Items);
        Assert.Equal("2", result.JobId);
        Assert.Equal(2, page.Page);
        Assert.Equal(1, page.PageSize);
        Assert.Equal(3, page.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_SortsByNumericField()
    {
        var storage = new InMemoryJobSearchStorage();
        await storage.SaveMetadataAsync("1", CreateMetadata("1", clienteId: "42"));
        await storage.SaveMetadataAsync("2", CreateMetadata("2", clienteId: "7"));
        await storage.SaveMetadataAsync("3", CreateMetadata("3", clienteId: "99"));

        var page = await storage.SearchAsync(new JobSearchQuery
        {
            SortBy = "ClienteId"
        });

        Assert.Equal(new[] { "2", "1", "3" }, page.Items.Select(item => item.JobId));
    }

    [Fact]
    public async Task DeleteMetadataAsync_RemovesResult()
    {
        var storage = new InMemoryJobSearchStorage();
        await storage.SaveMetadataAsync("1", CreateMetadata("1", documento: "123"));

        await storage.DeleteMetadataAsync("1");

        var page = await storage.SearchAsync(new JobSearchQuery
        {
            Filters = { ["Documento"] = "123" }
        });

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    private static JobSearchMetadata CreateMetadata(
        string jobId,
        string documento = "123",
        string clienteId = "42",
        string state = "Enqueued",
        DateTimeOffset? createdAt = null)
    {
        return new JobSearchMetadata
        {
            JobId = jobId,
            JobType = "ReportWorker",
            MethodName = "GerarRelatorioAsync",
            State = state,
            CreatedAt = createdAt ?? DateTimeOffset.Parse("2026-05-03T10:00:00Z"),
            Fields =
            {
                ["Documento"] = documento,
                ["ClienteId"] = clienteId
            }
        };
    }
}
