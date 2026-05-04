namespace Hangfire.Dashboard.JobSearch.Tests;

public sealed class InMemoryJobSearchStorageTests
{
    [Fact]
    public async Task SearchAsync_ReturnsResultsByOneField()
    {
        var storage = new InMemoryJobSearchStorage();
        await storage.SaveMetadataAsync("1", CreateMetadata("1", field1: "123", field2: "42"));
        await storage.SaveMetadataAsync("2", CreateMetadata("2", field1: "456", field2: "42"));

        var page = await storage.SearchAsync(new JobSearchQuery
        {
            Filters = { ["Field1"] = "123" }
        });

        var result = Assert.Single(page.Items);
        Assert.Equal("1", result.JobId);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_FiltersByJobId()
    {
        var storage = new InMemoryJobSearchStorage();
        await storage.SaveMetadataAsync("job-1", CreateMetadata("job-1", field1: "123"));
        await storage.SaveMetadataAsync("job-2", CreateMetadata("job-2", field1: "123"));

        var page = await storage.SearchAsync(new JobSearchQuery
        {
            JobId = "job-2"
        });

        var result = Assert.Single(page.Items);
        Assert.Equal("job-2", result.JobId);
    }

    [Fact]
    public async Task SearchAsync_ReturnsResultsByTwoFields()
    {
        var storage = new InMemoryJobSearchStorage();
        await storage.SaveMetadataAsync("1", CreateMetadata("1", field1: "123", field2: "42"));
        await storage.SaveMetadataAsync("2", CreateMetadata("2", field1: "123", field2: "99"));
        await storage.SaveMetadataAsync("3", CreateMetadata("3", field1: "456", field2: "42"));

        var page = await storage.SearchAsync(new JobSearchQuery
        {
            Filters =
            {
                ["Field1"] = "123",
                ["Field2"] = "42"
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
        await storage.SaveMetadataAsync("1", CreateMetadata("1", field2: "42"));
        await storage.SaveMetadataAsync("2", CreateMetadata("2", field2: "7"));
        await storage.SaveMetadataAsync("3", CreateMetadata("3", field2: "99"));

        var page = await storage.SearchAsync(new JobSearchQuery
        {
            SortBy = "Field2"
        });

        Assert.Equal(new[] { "2", "1", "3" }, page.Items.Select(item => item.JobId));
    }

    [Fact]
    public async Task SearchAsync_SortsByMultipleFields()
    {
        var storage = new InMemoryJobSearchStorage();
        await storage.SaveMetadataAsync("1", CreateMetadata("1", field2: "42", state: "Processing"));
        await storage.SaveMetadataAsync("2", CreateMetadata("2", field2: "7", state: "Succeeded"));
        await storage.SaveMetadataAsync("3", CreateMetadata("3", field2: "99", state: "Succeeded"));

        var page = await storage.SearchAsync(new JobSearchQuery
        {
            Sorts = new List<JobSearchSort>
            {
                new JobSearchSort { Field = "State", Descending = false },
                new JobSearchSort { Field = "Field2", Descending = true }
            }
        });

        Assert.Equal(new[] { "1", "3", "2" }, page.Items.Select(item => item.JobId));
    }

    [Fact]
    public async Task DeleteMetadataAsync_RemovesResult()
    {
        var storage = new InMemoryJobSearchStorage();
        await storage.SaveMetadataAsync("1", CreateMetadata("1", field1: "123"));

        await storage.DeleteMetadataAsync("1");

        var page = await storage.SearchAsync(new JobSearchQuery
        {
            Filters = { ["Field1"] = "123" }
        });

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task UpdateStateAsync_UpdatesExistingMetadata()
    {
        var storage = new InMemoryJobSearchStorage();
        await storage.SaveMetadataAsync("1", CreateMetadata("1", state: "Enqueued"));

        await storage.UpdateStateAsync("1", "Succeeded");

        var page = await storage.SearchAsync(new JobSearchQuery { State = "Succeeded" });
        var result = Assert.Single(page.Items);
        Assert.Equal("1", result.JobId);
    }

    [Fact]
    public async Task UpdateStateAsync_DoesNothingWhenJobDoesNotExist()
    {
        var storage = new InMemoryJobSearchStorage();

        await storage.UpdateStateAsync("missing", "Failed");

        var page = await storage.SearchAsync(new JobSearchQuery { State = "Failed" });
        Assert.Empty(page.Items);
    }

    private static JobSearchMetadata CreateMetadata(
        string jobId,
        string field1 = "123",
        string field2 = "42",
        string state = "Enqueued",
        DateTimeOffset? createdAt = null)
    {
        return new JobSearchMetadata
        {
            JobId = jobId,
            JobType = "WorkerA",
            MethodName = "RunJobAsync",
            State = state,
            CreatedAt = createdAt ?? DateTimeOffset.Parse("2026-05-03T10:00:00Z"),
            Fields =
            {
                ["Field1"] = field1,
                ["Field2"] = field2
            }
        };
    }
}
