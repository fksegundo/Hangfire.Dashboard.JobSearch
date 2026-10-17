namespace Hangfire.Dashboard.JobSearch.Tests;

public sealed class JobSearchDashboardRendererTests
{
    [Fact]
    public void Render_IncludesFilterableFieldInputs()
    {
        var model = CreateModel();

        var html = JobSearchDashboardRenderer.Render(model);

        Assert.Contains("name=\"f_Field1\"", html);
        Assert.Contains("name=\"f_Field2\"", html);
    }

    [Fact]
    public void Render_IncludesDisplayColumns()
    {
        var model = CreateModel();

        var html = JobSearchDashboardRenderer.Render(model);

        Assert.Contains("<th>Field1", html);
        Assert.Contains("<th>Field2", html);
    }

    [Fact]
    public void Render_IncludesNativeJobDetailsLink()
    {
        var model = CreateModel();

        var html = JobSearchDashboardRenderer.Render(model);

        Assert.Contains("/hangfire/jobs/details/123", html);
    }

    [Fact]
    public void Render_PreservesFiltersInPaginationUrl()
    {
        var model = CreateModel();
        model.Query.Page = 1;
        model.Result.Page = 1;
        model.Result.PageSize = 1;
        model.Result.TotalCount = 2;
        model.Query.Sorts.Add(new JobSearchSort { Field = "CreatedAt", Descending = true });

        var html = JobSearchDashboardRenderer.Render(model);

        Assert.Contains("f_Field1=12345678900", html);
        Assert.Contains("page=2", html);
        Assert.Contains("definition=WorkerA%7CRunJobAsync", html);
        Assert.Contains("name=\"sort\"", html);
    }

    [Fact]
    public void QueryMapper_MapsFieldsAndDefaults()
    {
        var options = new JobSearchOptions();
        var definition = new JobSearchDefinition
        {
            JobType = typeof(object),
            MethodName = "ToString"
        };
        definition.Fields.Add(new JobSearchField
        {
            Name = "Field1",
            Filterable = true,
            JsonPath = "$.Field1"
        });
        options.AddJobDefinition(definition);

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["jobId"] = "abc-123",
            ["f_Field1"] = "12345678900",
            ["page"] = "-1",
            ["pageSize"] = "0",
            ["state"] = "Succeeded"
        };

        var query = JobSearchDashboardQueryMapper.Map(
            key => values.TryGetValue(key, out var value) ? value : null,
            definition);

        Assert.Equal("12345678900", query.Filters["Field1"]);
        Assert.Equal("abc-123", query.JobId);
        Assert.Equal(1, query.Page);
        Assert.Equal(20, query.PageSize);
        Assert.Equal("Succeeded", query.State);
        Assert.Equal(typeof(object).FullName, query.JobType);
        Assert.Empty(query.Sorts);
    }

    [Fact]
    public void QueryMapper_DefaultsStateToProcessingWhenMissing()
    {
        var definition = new JobSearchDefinition
        {
            JobType = typeof(string),
            MethodName = "ToString"
        };

        var query = JobSearchDashboardQueryMapper.Map(_ => null, definition);

        Assert.Equal("Processing", query.State);
    }

    [Fact]
    public void Render_UsesIconSortLinks()
    {
        var model = CreateModel();
        var html = JobSearchDashboardRenderer.Render(model);

        Assert.Contains("title=\"Sort ascending\"", html);
        Assert.Contains("title=\"Add descending sort\"", html);
    }

    private static JobSearchDashboardViewModel CreateModel()
    {
        return new JobSearchDashboardViewModel
        {
            PathBase = "/hangfire",
            Query = new JobSearchQuery
            {
                Page = 1,
                PageSize = 20,
                State = "Processing",
                Filters = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Field1"] = "12345678900",
                    ["Field2"] = "42"
                }
            },
            SelectedDefinitionKey = "WorkerA|RunJobAsync",
            Definitions = new[]
            {
                new JobSearchDashboardViewModel.DefinitionOption
                {
                    Key = "WorkerA|RunJobAsync",
                    Label = "WorkerA.RunJobAsync"
                }
            },
            FilterableFields = new[] { "Field1", "Field2" },
            DisplayFields = new[] { "Field1", "Field2" },
            JobTypes = new[] { "WorkerA" },
            States = new[]
            {
                new JobSearchDashboardViewModel.StateOption { Value = "Processing", Label = "Processing (5)" },
                new JobSearchDashboardViewModel.StateOption { Value = "Succeeded", Label = "Succeeded (10)" }
            },
            Result = new JobSearchResultPage
            {
                Page = 1,
                PageSize = 20,
                TotalCount = 1,
                Items = new[]
                {
                    new JobSearchResult
                    {
                        JobId = "123",
                        State = "Succeeded",
                        JobType = "WorkerA",
                        MethodName = "RunJobAsync",
                        CreatedAt = DateTimeOffset.Parse("2026-05-03T12:00:00Z"),
                        Fields = new Dictionary<string, string?>(StringComparer.Ordinal)
                        {
                            ["Field1"] = "12345678900",
                            ["Field2"] = "42"
                        }
                    }
                }
            }
        };
    }
}
