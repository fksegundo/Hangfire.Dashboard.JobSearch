namespace Hangfire.Dashboard.JobSearch.Tests;

public sealed class JobSearchDashboardRendererTests
{
    [Fact]
    public void Render_IncludesFilterableFieldInputs()
    {
        var model = CreateModel();

        var html = JobSearchDashboardRenderer.Render(model);

        Assert.Contains("name=\"f_Documento\"", html);
        Assert.Contains("name=\"f_ClienteId\"", html);
    }

    [Fact]
    public void Render_IncludesDisplayColumns()
    {
        var model = CreateModel();

        var html = JobSearchDashboardRenderer.Render(model);

        Assert.Contains("<th>Documento</th>", html);
        Assert.Contains("<th>ClienteId</th>", html);
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

        var html = JobSearchDashboardRenderer.Render(model);

        Assert.Contains("f_Documento=12345678900", html);
        Assert.Contains("page=2", html);
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
            Name = "Documento",
            Filterable = true,
            JsonPath = "$.Documento"
        });
        options.AddJobDefinition(definition);

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["f_Documento"] = "12345678900",
            ["page"] = "-1",
            ["pageSize"] = "0",
            ["state"] = "Succeeded"
        };

        var query = JobSearchDashboardQueryMapper.Map(
            key => values.TryGetValue(key, out var value) ? value : null,
            options);

        Assert.Equal("12345678900", query.Filters["Documento"]);
        Assert.Equal(1, query.Page);
        Assert.Equal(20, query.PageSize);
        Assert.Equal("Succeeded", query.State);
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
                Filters = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Documento"] = "12345678900",
                    ["ClienteId"] = "42"
                }
            },
            FilterableFields = new[] { "Documento", "ClienteId" },
            DisplayFields = new[] { "Documento", "ClienteId" },
            JobTypes = new[] { "ReportWorker" },
            States = new[] { "Enqueued", "Succeeded" },
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
                        JobType = "ReportWorker",
                        MethodName = "GerarRelatorioAsync",
                        CreatedAt = DateTimeOffset.Parse("2026-05-03T12:00:00Z"),
                        Fields = new Dictionary<string, string?>(StringComparer.Ordinal)
                        {
                            ["Documento"] = "12345678900",
                            ["ClienteId"] = "42"
                        }
                    }
                }
            }
        };
    }
}
